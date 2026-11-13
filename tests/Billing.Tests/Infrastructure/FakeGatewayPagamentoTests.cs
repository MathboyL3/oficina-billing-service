using Billing.Application.Pagamentos;
using Billing.Infrastructure.MercadoPago;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Billing.Tests.Infrastructure;

public class FakeGatewayPagamentoTests
{
    private static FakeGatewayPagamento Gw(string comp) => new(Options.Create(new MercadoPagoOptions { PublicBaseUrl = "http://local", FakeComportamento = comp }));

    [Fact]
    public async Task Aprovar_por_padrao()
    {
        var gw = Gw("Aprovar");
        var pid = Guid.NewGuid();
        var os = Guid.NewGuid();
        var c = await gw.CriarCobrancaAsync(new CriarCobrancaRequest(pid, os, "x", 10m), default);
        c.LinkPagamento.Should().Be($"http://local/fake-checkout/{pid}");
        var info = await gw.BuscarPorReferenciaAsync(os.ToString(), default);
        info!.Status.Should().Be(StatusPagamentoGateway.Aprovado);
        (await gw.ObterPagamentoAsync(info.PaymentId, default)).ExternalReference.Should().Be(os.ToString());
        (await gw.EstornarAsync(info.PaymentId, null, default)).Sucesso.Should().BeTrue();
    }

    [Fact]
    public async Task ObterPagamento_desconhecido_lanca()
    {
        await FluentActions.Awaiting(() => Gw("Aprovar").ObterPagamentoAsync("nao-existe", default)).Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Estorno_ok_muda_status_para_estornado()
    {
        var gw = Gw("Aprovar");
        var os = Guid.NewGuid();
        await gw.CriarCobrancaAsync(new CriarCobrancaRequest(Guid.NewGuid(), os, "x", 10m), default);
        var info = (await gw.BuscarPorReferenciaAsync(os.ToString(), default))!;

        (await gw.EstornarAsync(info.PaymentId, null, default)).Sucesso.Should().BeTrue();

        (await gw.BuscarPorReferenciaAsync(os.ToString(), default))!.Status.Should().Be(StatusPagamentoGateway.Estornado);
    }

    [Fact]
    public async Task Recusar_e_FalharEstorno()
    {
        var os = Guid.NewGuid();
        await Gw("Recusar").CriarCobrancaAsync(new CriarCobrancaRequest(Guid.NewGuid(), os, "x", 10m), default);
        (await Gw("Recusar").BuscarPorReferenciaAsync(os.ToString(), default))!.Status.Should().Be(StatusPagamentoGateway.Recusado);
        (await Gw("FalharEstorno").EstornarAsync("qualquer", null, default)).Sucesso.Should().BeFalse();
        (await Gw("Aprovar").BuscarPorReferenciaAsync(Guid.NewGuid().ToString(), default)).Should().BeNull();
    }

    [Fact]
    public async Task Estorno_de_pagamento_desconhecido_retorna_404_como_o_real()
    {
        var r = await Gw("Aprovar").EstornarAsync("nao-existe", null, default);

        r.Sucesso.Should().BeFalse();
        r.Erro.Should().Be("404: pagamento não encontrado");
    }
}
