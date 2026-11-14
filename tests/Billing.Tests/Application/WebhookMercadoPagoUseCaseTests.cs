using Billing.Application.Common;
using Billing.Application.Pagamentos;
using Billing.Application.UseCases;
using Billing.Domain.Orcamentos;
using Billing.Domain.Pagamentos;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oficina.Contracts;

namespace Billing.Tests.Application;

public class WebhookMercadoPagoUseCaseTests
{
    private readonly Mock<IGatewayPagamento> _gw = new();
    private readonly Mock<IPagamentoRepository> _repo = new();
    private readonly Mock<IPublishEndpoint> _bus = new();
    private readonly Mock<IOrcamentoRepository> _orcs = new();
    private WebhookMercadoPagoUseCase Uc() => new(_gw.Object, _repo.Object, _orcs.Object, _bus.Object, NullLogger<WebhookMercadoPagoUseCase>.Instance);

    private Pagamento Pendente()
    {
        var p = Pagamento.Criar(Guid.NewGuid(), Guid.NewGuid(), 100m);
        _repo.Setup(r => r.ObterPorOsAsync(p.OrdemServicoId, It.IsAny<CancellationToken>())).ReturnsAsync(p);
        return p;
    }

    [Fact]
    public async Task Aprovado_publica_PagamentoConfirmado()
    {
        var p = Pendente();
        _gw.Setup(g => g.ObterPagamentoAsync("77", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagamentoInfo("77", StatusPagamentoGateway.Aprovado, p.OrdemServicoId.ToString(), "approved"));
        await Uc().ProcessarAsync("77", default);
        p.Status.Should().Be(StatusPagamento.Aprovado);
        _repo.Verify(r => r.AtualizarAsync(p, It.IsAny<CancellationToken>()), Times.Once);
        _bus.Verify(b => b.Publish(It.Is<PagamentoConfirmado>(m => m.PagamentoId == p.Id && m.OrdemServicoId == p.OrdemServicoId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Concorrencia_na_gravacao_conta_como_ja_processado_e_nao_publica()
    {
        var p = Pendente();
        _gw.Setup(g => g.ObterPagamentoAsync("79", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagamentoInfo("79", StatusPagamentoGateway.Aprovado, p.OrdemServicoId.ToString(), "approved"));
        _repo.Setup(r => r.AtualizarAsync(p, It.IsAny<CancellationToken>())).ThrowsAsync(new ConcorrenciaException("Pagamento", p.Id));

        await Uc().ProcessarAsync("79", default);

        _bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Concorrencia_na_recusa_tambem_nao_publica()
    {
        var p = Pendente();
        _gw.Setup(g => g.ObterPagamentoAsync("80", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagamentoInfo("80", StatusPagamentoGateway.Recusado, p.OrdemServicoId.ToString(), "rejected"));
        _repo.Setup(r => r.AtualizarAsync(p, It.IsAny<CancellationToken>())).ThrowsAsync(new ConcorrenciaException("Pagamento", p.Id));

        await Uc().ProcessarAsync("80", default);

        _bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Recusado_publica_PagamentoRecusado()
    {
        var p = Pendente();
        _gw.Setup(g => g.ObterPagamentoAsync("78", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagamentoInfo("78", StatusPagamentoGateway.Recusado, p.OrdemServicoId.ToString(), "rejected"));
        await Uc().ProcessarAsync("78", default);
        _bus.Verify(b => b.Publish(It.Is<PagamentoRecusado>(m => m.PagamentoId == p.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Duplicado_ou_desconhecido_ou_pendente_nao_publica()
    {
        var p = Pendente();
        p.Aprovar("77");
        _gw.Setup(g => g.ObterPagamentoAsync("77", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagamentoInfo("77", StatusPagamentoGateway.Aprovado, p.OrdemServicoId.ToString(), "approved"));
        _gw.Setup(g => g.ObterPagamentoAsync("x", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagamentoInfo("x", StatusPagamentoGateway.Aprovado, Guid.NewGuid().ToString(), "approved"));
        _gw.Setup(g => g.ObterPagamentoAsync("y", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagamentoInfo("y", StatusPagamentoGateway.Pendente, p.OrdemServicoId.ToString(), "in_process"));
        _gw.Setup(g => g.ObterPagamentoAsync("z", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagamentoInfo("z", StatusPagamentoGateway.Aprovado, "nao-eh-guid", "approved"));

        await Uc().ProcessarAsync("77", default);
        await Uc().ProcessarAsync("x", default);
        await Uc().ProcessarAsync("y", default);
        await Uc().ProcessarAsync("z", default);

        _bus.VerifyNoOtherCalls();
    }

    private Orcamento OrcamentoCom(Pagamento p, bool cancelar, bool recusar = false)
    {
        var o = Orcamento.Gerar(p.OrdemServicoId, Guid.NewGuid(), new[] { new ItemOrcamentoDominio("Servico", Guid.NewGuid(), "x", 100m, 1) }, TimeSpan.FromDays(1));
        if (cancelar) o.Cancelar();
        if (recusar) o.Recusar("nao");
        _orcs.Setup(r => r.ObterPorOsAsync(p.OrdemServicoId, It.IsAny<CancellationToken>())).ReturnsAsync(o);
        return o;
    }

    private void Aprovado(Pagamento p, string id) =>
        _gw.Setup(g => g.ObterPagamentoAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagamentoInfo(id, StatusPagamentoGateway.Aprovado, p.OrdemServicoId.ToString(), "approved"));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Aprovado_com_orcamento_cancelado_ou_recusado_estorna_e_nao_confirma(bool cancelado)
    {
        var p = Pendente();
        OrcamentoCom(p, cancelado, !cancelado);
        Aprovado(p, "90");
        _gw.Setup(g => g.EstornarAsync("90", null, It.IsAny<CancellationToken>())).ReturnsAsync(new EstornoResult(true, null));

        await Uc().ProcessarAsync("90", default);

        p.Status.Should().Be(StatusPagamento.Estornado);
        _bus.Verify(b => b.Publish(It.IsAny<PagamentoConfirmado>(), It.IsAny<CancellationToken>()), Times.Never);
        _bus.Verify(b => b.Publish(It.Is<PagamentoEstornado>(m => m.PagamentoId == p.Id && m.OrdemServicoId == p.OrdemServicoId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Aprovado_com_orcamento_cancelado_e_estorno_falho_publica_EstornoFalhou_e_fica_pendente()
    {
        var p = Pendente();
        OrcamentoCom(p, true);
        Aprovado(p, "91");
        _gw.Setup(g => g.EstornarAsync("91", null, It.IsAny<CancellationToken>())).ReturnsAsync(new EstornoResult(false, "boom"));

        await Uc().ProcessarAsync("91", default);

        p.Status.Should().Be(StatusPagamento.EstornoPendente);
        _bus.Verify(b => b.Publish(It.IsAny<PagamentoConfirmado>(), It.IsAny<CancellationToken>()), Times.Never);
        _bus.Verify(b => b.Publish(It.Is<EstornoFalhou>(m => m.PagamentoId == p.Id && m.Motivo == "boom"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Aprovado_com_orcamento_aprovado_segue_fluxo_normal()
    {
        var p = Pendente();
        var o = OrcamentoCom(p, false);
        o.Aprovar(DateTimeOffset.UtcNow);
        Aprovado(p, "92");

        await Uc().ProcessarAsync("92", default);

        p.Status.Should().Be(StatusPagamento.Aprovado);
        _gw.Verify(g => g.EstornarAsync(It.IsAny<string>(), It.IsAny<decimal?>(), It.IsAny<CancellationToken>()), Times.Never);
        _bus.Verify(b => b.Publish(It.Is<PagamentoConfirmado>(m => m.PagamentoId == p.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Aprovado_apos_cancelamento_registra_EstornoPendente_antes_de_chamar_o_gateway()
    {
        var p = Pendente();
        OrcamentoCom(p, true);
        Aprovado(p, "93");
        var statusNaGravacao = new List<StatusPagamento>();
        _repo.Setup(r => r.AtualizarAsync(p, It.IsAny<CancellationToken>())).Callback<Pagamento, CancellationToken>((x, _) => statusNaGravacao.Add(x.Status)).Returns(Task.CompletedTask);
        StatusPagamento? statusNoEstorno = null;
        _gw.Setup(g => g.EstornarAsync("93", null, It.IsAny<CancellationToken>()))
            .Callback(() => statusNoEstorno = p.Status).ReturnsAsync(new EstornoResult(true, null));

        await Uc().ProcessarAsync("93", default);

        statusNaGravacao.Should().Equal(StatusPagamento.EstornoPendente, StatusPagamento.Estornado);
        statusNoEstorno.Should().Be(StatusPagamento.EstornoPendente);
    }

    [Fact]
    public async Task Aprovado_apos_cancelamento_loga_alerta_pagamento_apos_cancelamento()
    {
        var p = Pendente();
        OrcamentoCom(p, true);
        Aprovado(p, "94");
        _gw.Setup(g => g.EstornarAsync("94", null, It.IsAny<CancellationToken>())).ReturnsAsync(new EstornoResult(true, null));
        var log = new Mock<ILogger<WebhookMercadoPagoUseCase>>();
        log.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        var uc = new WebhookMercadoPagoUseCase(_gw.Object, _repo.Object, _orcs.Object, _bus.Object, log.Object);

        await uc.ProcessarAsync("94", default);

        log.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("pagamento_apos_cancelamento") && v.ToString()!.Contains(p.Id.ToString())),
            It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }
}
