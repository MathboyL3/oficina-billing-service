using System.Net;
using System.Text;
using System.Text.Json;
using Billing.Application.Pagamentos;
using Billing.Infrastructure.MercadoPago;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Billing.Tests.Infrastructure;

public class MercadoPagoGatewayTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> Bodies { get; } = new();
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _resp;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> resp) => _resp = resp;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Requests.Add(req);
            Bodies.Add(req.Content is null ? "" : await req.Content.ReadAsStringAsync(ct));
            return _resp(req);
        }
    }

    private static HttpResponseMessage Json(object o, HttpStatusCode code = HttpStatusCode.OK)
        => new(code) { Content = new StringContent(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json") };

    private static (MercadoPagoGateway gw, StubHandler h) Criar(Func<HttpRequestMessage, HttpResponseMessage> resp, bool sandbox = true)
    {
        var h = new StubHandler(resp);
        var opt = Options.Create(new MercadoPagoOptions { AccessToken = "TEST-token", PublicBaseUrl = "https://pub.example", Sandbox = sandbox });
        var http = new HttpClient(h) { BaseAddress = new Uri("https://api.mercadopago.com") };
        return (new MercadoPagoGateway(http, opt), h);
    }

    [Fact]
    public async Task CriarCobranca_envia_preferencia_com_idempotencia_e_retorna_link_sandbox()
    {
        var (gw, h) = Criar(_ => Json(new { id = "pref-123", init_point = "https://mp/prod", sandbox_init_point = "https://mp/sandbox" }, HttpStatusCode.Created));
        var pid = Guid.NewGuid();

        var r = await gw.CriarCobrancaAsync(new CriarCobrancaRequest(pid, Guid.NewGuid(), "OS 1", 151m), CancellationToken.None);

        r.PreferenceId.Should().Be("pref-123");
        r.LinkPagamento.Should().Be("https://mp/sandbox");
        var req = h.Requests.Single();
        req.Method.Should().Be(HttpMethod.Post);
        req.RequestUri!.AbsolutePath.Should().Be("/checkout/preferences");
        req.Headers.Authorization!.ToString().Should().Be("Bearer TEST-token");
        req.Headers.GetValues("X-Idempotency-Key").Single().Should().Be(pid.ToString());
        using var body = JsonDocument.Parse(h.Bodies.Single());
        body.RootElement.GetProperty("external_reference").GetString().Should().Be(pid.ToString());
        body.RootElement.GetProperty("notification_url").GetString().Should().Be("https://pub.example/webhooks/mercadopago");
        body.RootElement.GetProperty("items")[0].GetProperty("unit_price").GetDecimal().Should().Be(151m);
    }

    [Fact]
    public async Task CriarCobranca_em_producao_usa_init_point()
    {
        var (gw, _) = Criar(_ => Json(new { id = "p", init_point = "https://mp/prod", sandbox_init_point = "https://mp/sandbox" }), sandbox: false);
        (await gw.CriarCobrancaAsync(new CriarCobrancaRequest(Guid.NewGuid(), Guid.NewGuid(), "x", 1m), default)).LinkPagamento.Should().Be("https://mp/prod");
    }

    [Theory]
    [InlineData("approved", StatusPagamentoGateway.Aprovado)]
    [InlineData("rejected", StatusPagamentoGateway.Recusado)]
    [InlineData("cancelled", StatusPagamentoGateway.Recusado)]
    [InlineData("refunded", StatusPagamentoGateway.Estornado)]
    [InlineData("in_process", StatusPagamentoGateway.Pendente)]
    public async Task ObterPagamento_mapeia_status(string mp, StatusPagamentoGateway esperado)
    {
        var (gw, h) = Criar(_ => Json(new { id = 999, status = mp, external_reference = "ref" }));
        var p = await gw.ObterPagamentoAsync("999", default);
        p.Status.Should().Be(esperado);
        p.ExternalReference.Should().Be("ref");
        h.Requests.Single().RequestUri!.AbsolutePath.Should().Be("/v1/payments/999");
    }

    [Fact]
    public async Task BuscarPorReferencia_retorna_o_mais_recente_ou_null()
    {
        var (gw, h) = Criar(_ => Json(new { results = new[] { new { id = 7, status = "approved", external_reference = "r" } } }));
        (await gw.BuscarPorReferenciaAsync("r", default))!.PaymentId.Should().Be("7");
        h.Requests.Single().RequestUri!.Query.Should().Contain("external_reference=r");

        var (gw2, _) = Criar(_ => Json(new { results = Array.Empty<object>() }));
        (await gw2.BuscarPorReferenciaAsync("r", default)).Should().BeNull();
    }

    [Fact]
    public async Task Estornar_ok_e_erro()
    {
        var (gw, h) = Criar(_ => Json(new { id = 1, status = "approved" }, HttpStatusCode.Created));
        (await gw.EstornarAsync("55", null, default)).Sucesso.Should().BeTrue();
        h.Requests.Single().RequestUri!.AbsolutePath.Should().Be("/v1/payments/55/refunds");

        var (gw2, _) = Criar(_ => Json(new { message = "Payment not found" }, HttpStatusCode.NotFound));
        var r = await gw2.EstornarAsync("55", null, default);
        r.Sucesso.Should().BeFalse();
        r.Erro.Should().Contain("404");
    }

    [Fact]
    public async Task Estornar_parcial_envia_amount()
    {
        var (gw, h) = Criar(_ => Json(new { id = 1 }, HttpStatusCode.Created));
        (await gw.EstornarAsync("55", 10.5m, default)).Sucesso.Should().BeTrue();
        using var body = JsonDocument.Parse(h.Bodies.Single());
        body.RootElement.GetProperty("amount").GetDecimal().Should().Be(10.5m);
    }

    [Fact]
    public async Task Estornar_falha_de_transporte_vira_resultado_de_erro()
    {
        foreach (Exception ex in new Exception[] { new HttpRequestException("conexao caiu"), new TaskCanceledException("timeout") })
        {
            var (gw, _) = Criar(_ => throw ex);
            var r = await gw.EstornarAsync("55", null, CancellationToken.None);
            r.Sucesso.Should().BeFalse();
            r.Erro.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task Estornar_com_cancelamento_real_lanca()
    {
        using var cts = new CancellationTokenSource();
        var (gw, _) = Criar(_ => { cts.Cancel(); throw new TaskCanceledException(); });
        await FluentActions.Awaiting(() => gw.EstornarAsync("55", null, cts.Token)).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ObterPagamento_404_lanca()
    {
        var (gw, _) = Criar(_ => Json(new { message = "not found" }, HttpStatusCode.NotFound));
        await FluentActions.Awaiting(() => gw.ObterPagamentoAsync("1", default)).Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task CriarCobranca_sandbox_sem_sandbox_init_point_usa_init_point()
    {
        var (gw, _) = Criar(_ => Json(new { id = "p", init_point = "https://mp/prod" }), sandbox: true);
        (await gw.CriarCobrancaAsync(new CriarCobrancaRequest(Guid.NewGuid(), Guid.NewGuid(), "x", 1m), default)).LinkPagamento.Should().Be("https://mp/prod");
    }

    [Fact]
    public async Task CriarCobranca_sem_link_lanca()
    {
        var (gw, _) = Criar(_ => Json(new { id = "p" }));
        await FluentActions.Awaiting(() => gw.CriarCobrancaAsync(new CriarCobrancaRequest(Guid.NewGuid(), Guid.NewGuid(), "x", 1m), default))
            .Should().ThrowAsync<HttpRequestException>().WithMessage("*init_point*");
    }

    [Fact]
    public async Task Erro_http_na_criacao_lanca()
    {
        var (gw, _) = Criar(_ => Json(new { message = "invalid" }, HttpStatusCode.BadRequest));
        await FluentActions.Awaiting(() => gw.CriarCobrancaAsync(new CriarCobrancaRequest(Guid.NewGuid(), Guid.NewGuid(), "x", 1m), default))
            .Should().ThrowAsync<HttpRequestException>();
    }
}
