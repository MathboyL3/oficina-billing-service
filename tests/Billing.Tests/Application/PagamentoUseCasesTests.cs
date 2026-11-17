using Billing.Application.Common;
using Billing.Application.Pagamentos;
using Billing.Application.UseCases;
using Billing.Domain.Common;
using Billing.Domain.Pagamentos;
using FluentAssertions;
using MassTransit;
using Moq;
using Oficina.Contracts;

namespace Billing.Tests.Application;

public class PagamentoUseCasesTests
{
    private readonly Mock<IPagamentoRepository> _repo = new();
    private readonly Mock<IGatewayPagamento> _gw = new();
    private readonly Mock<IPublishEndpoint> _bus = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private PagamentoUseCases Uc() => new(_repo.Object, _gw.Object, _bus.Object, _uow.Object);

    private Pagamento Com(StatusPagamento status)
    {
        var p = Pagamento.Criar(Guid.NewGuid(), Guid.NewGuid(), 100m);
        if (status != StatusPagamento.Pendente)
        {
            p.Aprovar("55");
            if (status == StatusPagamento.EstornoPendente) p.MarcarEstornoPendente();
            if (status == StatusPagamento.Estornado) p.MarcarEstornado();
        }
        _repo.Setup(r => r.ObterPorIdAsync(p.Id, It.IsAny<CancellationToken>())).ReturnsAsync(p);
        return p;
    }

    [Fact]
    public async Task Reprocessar_com_sucesso_marca_Estornado_publica_e_comita()
    {
        var p = Com(StatusPagamento.EstornoPendente);
        _gw.Setup(g => g.EstornarAsync("55", null, It.IsAny<CancellationToken>())).ReturnsAsync(new EstornoResult(true, null));

        var r = await Uc().ReprocessarEstornoAsync(p.Id);

        p.Status.Should().Be(StatusPagamento.Estornado);
        r.Id.Should().Be(p.Id);
        _bus.Verify(b => b.Publish(It.Is<PagamentoEstornado>(m => m.PagamentoId == p.Id), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reprocessar_com_falha_continua_EstornoPendente_e_publica_EstornoFalhou()
    {
        var p = Com(StatusPagamento.EstornoPendente);
        _gw.Setup(g => g.EstornarAsync("55", null, It.IsAny<CancellationToken>())).ReturnsAsync(new EstornoResult(false, "boom"));
        _gw.Setup(g => g.ObterPagamentoAsync("55", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagamentoInfo("55", StatusPagamentoGateway.Aprovado, p.OrdemServicoId.ToString(), "approved"));

        await Uc().ReprocessarEstornoAsync(p.Id);

        p.Status.Should().Be(StatusPagamento.EstornoPendente);
        _bus.Verify(b => b.Publish(It.Is<EstornoFalhou>(m => m.PagamentoId == p.Id), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reprocessar_com_MP_ja_estornado_marca_Estornado()
    {
        var p = Com(StatusPagamento.EstornoPendente);
        _gw.Setup(g => g.EstornarAsync("55", null, It.IsAny<CancellationToken>())).ReturnsAsync(new EstornoResult(false, "ja estornado"));
        _gw.Setup(g => g.ObterPagamentoAsync("55", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagamentoInfo("55", StatusPagamentoGateway.Estornado, p.OrdemServicoId.ToString(), "refunded"));

        await Uc().ReprocessarEstornoAsync(p.Id);

        p.Status.Should().Be(StatusPagamento.Estornado);
    }

    [Fact]
    public async Task Reprocessar_com_MP_refunded_marca_Estornado_sem_chamar_EstornarAsync()
    {
        var p = Com(StatusPagamento.EstornoPendente);
        _gw.Setup(g => g.ObterPagamentoAsync("55", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagamentoInfo("55", StatusPagamentoGateway.Estornado, p.OrdemServicoId.ToString(), "refunded"));

        await Uc().ReprocessarEstornoAsync(p.Id);

        p.Status.Should().Be(StatusPagamento.Estornado);
        _gw.Verify(g => g.EstornarAsync(It.IsAny<string>(), It.IsAny<decimal?>(), It.IsAny<CancellationToken>()), Times.Never);
        _bus.Verify(b => b.Publish(It.Is<PagamentoEstornado>(m => m.PagamentoId == p.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reprocessar_com_consulta_previa_falhando_ainda_estorna()
    {
        var p = Com(StatusPagamento.EstornoPendente);
        _gw.Setup(g => g.ObterPagamentoAsync("55", It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("mp fora"));
        _gw.Setup(g => g.EstornarAsync("55", null, It.IsAny<CancellationToken>())).ReturnsAsync(new EstornoResult(true, null));

        await Uc().ReprocessarEstornoAsync(p.Id);

        p.Status.Should().Be(StatusPagamento.Estornado);
        _gw.Verify(g => g.EstornarAsync("55", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(StatusPagamento.Pendente)]
    [InlineData(StatusPagamento.Aprovado)]
    [InlineData(StatusPagamento.Estornado)]
    public async Task Reprocessar_em_outro_status_lanca_Conflict_sem_tocar_no_gateway(StatusPagamento status)
    {
        var p = Com(status);

        var act = () => Uc().ReprocessarEstornoAsync(p.Id);

        await act.Should().ThrowAsync<ConflictException>();
        _gw.VerifyNoOtherCalls();
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reprocessar_pagamento_inexistente_lanca_NotFound()
    {
        var act = () => Uc().ReprocessarEstornoAsync(Guid.NewGuid());
        await act.Should().ThrowAsync<NotFoundException>();
    }
}