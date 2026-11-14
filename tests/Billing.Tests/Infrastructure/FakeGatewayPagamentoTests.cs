using Billing.Application.Pagamentos;
using Billing.Infrastructure.MercadoPago;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Billing.Tests.Infrastructure;

public class FakeGatewayPagamentoTests
{
    private static FakeGatewayPagamento Gw(string comp) => new(Options.Create(new MercadoPagoOptions { PublicBaseUrl = "http://local", FakeComportamento = comp }));

    private static string PaymentId(Guid pagamentoId) => $"fake-{pagamentoId:N}";

    [Fact]
    public async Task CriarCobranca_devolve_preferencia_e_link_sem_gravar_estado()
    {
        var pid = Guid.NewGuid();
        var c = await Gw("Aprovar").CriarCobrancaAsync(new CriarCobrancaRequest(pid, Guid.NewGuid(), "x", 10m), default);
        c.PreferenceId.Should().Be($"fake-pref-{pid}");
        c.LinkPagamento.Should().Be($"http://local/fake-checkout/{pid}");
    }

    [Fact]
    public async Task ObterPagamento_aprova_por_padrao_e_recusa_quando_configurado()
    {
        var id = PaymentId(Guid.NewGuid());
        var ok = await Gw("Aprovar").ObterPagamentoAsync(id, default);
        ok.Should().Be(new PagamentoInfo(id, StatusPagamentoGateway.Aprovado, null, "Aprovado"));
        (await Gw("Recusar").ObterPagamentoAsync(id, default)).Status.Should().Be(StatusPagamentoGateway.Recusado);
    }

    [Theory]
    [InlineData("nao-existe")]
    [InlineData("fake-xyz")]
    [InlineData("123456")]
    public async Task ObterPagamento_invalido_lanca_404(string id)
    {
        var ex = await FluentActions.Awaiting(() => Gw("Aprovar").ObterPagamentoAsync(id, default)).Should().ThrowAsync<HttpRequestException>();
        ex.Which.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task BuscarPorReferencia_e_sempre_nulo()
        => (await Gw("Aprovar").BuscarPorReferenciaAsync(Guid.NewGuid().ToString(), default)).Should().BeNull();

    [Fact]
    public async Task Estorno_de_pagamento_valido_funciona_em_outra_instancia_replica()
    {
        var pid = Guid.NewGuid();
        await Gw("Aprovar").CriarCobrancaAsync(new CriarCobrancaRequest(pid, Guid.NewGuid(), "x", 10m), default);

        // outra instância (réplica/restart) não perde o pagamento
        var r = await Gw("Aprovar").EstornarAsync(PaymentId(pid), null, default);

        r.Sucesso.Should().BeTrue();
    }

    [Fact]
    public async Task FalharEstorno_falha_e_id_invalido_retorna_404_como_o_real()
    {
        (await Gw("FalharEstorno").EstornarAsync(PaymentId(Guid.NewGuid()), null, default)).Sucesso.Should().BeFalse();

        var r = await Gw("Aprovar").EstornarAsync("nao-existe", null, default);

        r.Sucesso.Should().BeFalse();
        r.Erro.Should().Be("404: pagamento não encontrado");
    }
}