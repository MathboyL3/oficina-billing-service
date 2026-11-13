using Billing.Application.Pagamentos;
using Billing.Application.UseCases;
using Billing.Domain.Pagamentos;
using FluentAssertions;
using MassTransit;
using Moq;
using Oficina.Contracts;

namespace Billing.Tests.Application;

public class WebhookMercadoPagoUseCaseTests
{
    private readonly Mock<IGatewayPagamento> _gw = new();
    private readonly Mock<IPagamentoRepository> _repo = new();
    private readonly Mock<IPublishEndpoint> _bus = new();
    private WebhookMercadoPagoUseCase Uc() => new(_gw.Object, _repo.Object, _bus.Object);

    private Pagamento Pendente()
    {
        var p = Pagamento.Criar(Guid.NewGuid(), Guid.NewGuid(), 100m);
        _repo.Setup(r => r.ObterPorIdAsync(p.Id, It.IsAny<CancellationToken>())).ReturnsAsync(p);
        return p;
    }

    [Fact]
    public async Task Aprovado_publica_PagamentoConfirmado()
    {
        var p = Pendente();
        _gw.Setup(g => g.ObterPagamentoAsync("77", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagamentoInfo("77", StatusPagamentoGateway.Aprovado, p.Id.ToString(), "approved"));
        await Uc().ProcessarAsync("77", default);
        p.Status.Should().Be(StatusPagamento.Aprovado);
        _repo.Verify(r => r.AtualizarAsync(p, It.IsAny<CancellationToken>()), Times.Once);
        _bus.Verify(b => b.Publish(It.Is<PagamentoConfirmado>(m => m.PagamentoId == p.Id && m.OrdemServicoId == p.OrdemServicoId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Recusado_publica_PagamentoRecusado()
    {
        var p = Pendente();
        _gw.Setup(g => g.ObterPagamentoAsync("78", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagamentoInfo("78", StatusPagamentoGateway.Recusado, p.Id.ToString(), "rejected"));
        await Uc().ProcessarAsync("78", default);
        _bus.Verify(b => b.Publish(It.Is<PagamentoRecusado>(m => m.PagamentoId == p.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Duplicado_ou_desconhecido_ou_pendente_nao_publica()
    {
        var p = Pendente();
        p.Aprovar("77");
        _gw.Setup(g => g.ObterPagamentoAsync("77", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagamentoInfo("77", StatusPagamentoGateway.Aprovado, p.Id.ToString(), "approved"));
        _gw.Setup(g => g.ObterPagamentoAsync("x", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagamentoInfo("x", StatusPagamentoGateway.Aprovado, Guid.NewGuid().ToString(), "approved"));
        _gw.Setup(g => g.ObterPagamentoAsync("y", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagamentoInfo("y", StatusPagamentoGateway.Pendente, p.Id.ToString(), "in_process"));
        _gw.Setup(g => g.ObterPagamentoAsync("z", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagamentoInfo("z", StatusPagamentoGateway.Aprovado, "nao-eh-guid", "approved"));

        await Uc().ProcessarAsync("77", default);
        await Uc().ProcessarAsync("x", default);
        await Uc().ProcessarAsync("y", default);
        await Uc().ProcessarAsync("z", default);

        _bus.VerifyNoOtherCalls();
    }
}