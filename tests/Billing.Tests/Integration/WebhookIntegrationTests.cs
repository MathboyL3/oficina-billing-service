using System.Net;
using System.Net.Http.Json;
using Billing.Application.Pagamentos;
using Billing.Domain.Pagamentos;
using FluentAssertions;
using Moq;
using Oficina.Contracts;

namespace Billing.Tests.Integration;

public class WebhookIntegrationTests : IClassFixture<BillingApiFactory>
{
    private readonly BillingApiFactory _f;
    public WebhookIntegrationTests(BillingApiFactory f) => _f = f;

    private static string Assinar(string dataId, string requestId, string? ts = null, string secret = BillingApiFactory.WebhookSecret)
    {
        ts ??= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var v1 = AssinaturaWebhook.Calcular($"id:{dataId};request-id:{requestId};ts:{ts};", secret);
        return $"ts={ts},v1={v1}";
    }

    private Task<HttpResponseMessage> Postar(string dataId, string? assinatura, string requestId = "req-1")
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/webhooks/mercadopago?data.id={dataId}&type=payment")
        {
            Content = JsonContent.Create(new { type = "payment", data = new { id = dataId } })
        };
        if (assinatura is not null) req.Headers.Add("x-signature", assinatura);
        req.Headers.Add("x-request-id", requestId);
        return _f.CreateClient().SendAsync(req);
    }

    [Fact]
    public async Task Sem_header_de_assinatura_401_e_nao_consulta_o_gateway()
    {
        var r = await Postar("101", null);

        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _f.Gateway.Verify(g => g.ObterPagamentoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Assinatura_invalida_401_e_nao_consulta_o_gateway()
    {
        var r = await Postar("102", Assinar("102", "req-1", secret: "outro-segredo"));

        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _f.Gateway.Verify(g => g.ObterPagamentoAsync("102", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Ts_malformado_401()
    {
        var r = await Postar("103", Assinar("103", "req-1", ts: "nao-numerico"));

        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _f.Gateway.Verify(g => g.ObterPagamentoAsync("103", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Assinatura_valida_aprova_e_publica_PagamentoConfirmado()
    {
        var pag = Pagamento.Criar(Guid.NewGuid(), Guid.NewGuid(), 50m);
        await _f.Pagamentos.InserirAsync(pag);
        _f.Gateway.Setup(g => g.ObterPagamentoAsync("104", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagamentoInfo("104", StatusPagamentoGateway.Aprovado, pag.OrdemServicoId.ToString(), "approved"));

        var r = await Postar("104", Assinar("104", "req-1"));

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        pag.Status.Should().Be(StatusPagamento.Aprovado);
        _f.Bus.Verify(b => b.Publish(It.Is<PagamentoConfirmado>(m => m.PagamentoId == pag.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Assinatura_valida_pagamento_desconhecido_no_gateway_200()
    {
        _f.Gateway.Setup(g => g.ObterPagamentoAsync("105", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("nao encontrado", null, HttpStatusCode.NotFound));

        var r = await Postar("105", Assinar("105", "req-1"));

        r.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Corpo_que_nao_e_json_com_assinatura_invalida_continua_401()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/webhooks/mercadopago") { Content = new StringContent("isto nao e json") };

        (await _f.CreateClient().SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Webhook_nao_exige_jwt_e_tipo_diferente_de_payment_so_responde_200()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/webhooks/mercadopago?data.id=106&type=merchant_order");
        req.Headers.Add("x-signature", Assinar("106", "req-1"));
        req.Headers.Add("x-request-id", "req-1");

        var r = await _f.CreateClient().SendAsync(req);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        _f.Gateway.Verify(g => g.ObterPagamentoAsync("106", It.IsAny<CancellationToken>()), Times.Never);
    }
}