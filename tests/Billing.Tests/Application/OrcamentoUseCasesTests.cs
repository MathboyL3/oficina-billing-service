using Billing.Application.Common;
using Billing.Application.DTOs;
using Billing.Application.UseCases;
using Billing.Domain.Common;
using Billing.Domain.Orcamentos;
using Billing.Domain.Pagamentos;
using FluentAssertions;
using MassTransit;
using Moq;
using Oficina.Contracts;

namespace Billing.Tests.Application;

public class OrcamentoUseCasesTests
{
    private readonly Mock<IOrcamentoRepository> _orcs = new();
    private readonly Mock<IPagamentoRepository> _pags = new();
    private readonly Mock<IPublishEndpoint> _bus = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly List<string> _ordem = new();

    private OrcamentoUseCases Uc()
    {
        _bus.Setup(b => b.Publish(It.IsAny<It.IsAnyType>(), It.IsAny<CancellationToken>()))
            .Callback((object m, CancellationToken _) => _ordem.Add(m.GetType().Name)).Returns(Task.CompletedTask);
        _uow.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Callback(() => _ordem.Add("commit")).ReturnsAsync(1);
        return new(_orcs.Object, _pags.Object, _bus.Object, _uow.Object);
    }

    private Orcamento Pendente(TimeSpan? validade = null)
    {
        var o = Orcamento.Gerar(Guid.NewGuid(), Guid.NewGuid(), new[] { new ItemOrcamentoDominio("Peca", Guid.NewGuid(), "p", 10m, 2) }, validade ?? TimeSpan.FromHours(1));
        _orcs.Setup(r => r.ObterPorIdAsync(o.Id, It.IsAny<CancellationToken>())).ReturnsAsync(o);
        return o;
    }

    [Fact]
    public async Task Aprovar_publica_OrcamentoAprovado()
    {
        var o = Pendente();
        var r = await Uc().AprovarAsync(o.Id, o.ClienteId, default);
        r.Status.Should().Be("Aprovado");
        r.ValorTotal.Should().Be(20m);
        _orcs.Verify(x => x.AtualizarAsync(o, It.IsAny<CancellationToken>()), Times.Once);
        _ordem.Should().Equal("OrcamentoAprovado", "commit");
    }

    [Fact]
    public async Task Aprovar_por_outro_cliente_lanca_Forbidden()
    {
        var o = Pendente();
        var act = () => Uc().AprovarAsync(o.Id, Guid.NewGuid(), default);
        await act.Should().ThrowAsync<ForbiddenException>();
        o.Status.Should().Be(StatusOrcamento.Pendente);
        _ordem.Should().BeEmpty();
    }

    [Fact]
    public async Task Aprovar_expirado_lanca_Domain()
    {
        var o = Pendente(TimeSpan.FromHours(-1));
        var act = () => Uc().AprovarAsync(o.Id, null, default);
        await act.Should().ThrowAsync<DomainException>();
        _ordem.Should().BeEmpty();
    }

    [Fact]
    public async Task Recusar_publica_OrcamentoRecusado()
    {
        var o = Pendente();
        var r = await Uc().RecusarAsync(o.Id, new RecusarOrcamentoRequest("caro"), o.ClienteId, default);
        r.Status.Should().Be("Recusado");
        r.MotivoRecusa.Should().Be("caro");
        _ordem.Should().Equal("OrcamentoRecusado", "commit");
    }

    [Fact]
    public async Task Obter_inexistente_NotFound()
    {
        var act = () => Uc().ObterAsync(Guid.NewGuid(), null, default);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Obter_e_ListarPorOs_mapeiam()
    {
        var o = Pendente();
        _orcs.Setup(r => r.ListarPorOsAsync(o.OrdemServicoId, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { o });
        (await Uc().ObterAsync(o.Id, null, default)).Itens.Should().HaveCount(1);
        (await Uc().ListarPorOsAsync(o.OrdemServicoId, null, default)).Should().ContainSingle(x => x.Id == o.Id);
    }

    [Fact]
    public async Task ObterPagamento_mapeia_e_inexistente_NotFound()
    {
        var p = Pagamento.Criar(Guid.NewGuid(), Guid.NewGuid(), 10m);
        p.RegistrarPreferencia("p", "http://l");
        _pags.Setup(r => r.ObterPorIdAsync(p.Id, It.IsAny<CancellationToken>())).ReturnsAsync(p);
        var r = await Uc().ObterPagamentoAsync(p.Id, null, default);
        r.LinkPagamento.Should().Be("http://l");
        r.Status.Should().Be("Pendente");
        var act = () => Uc().ObterPagamentoAsync(Guid.NewGuid(), null, default);
        await act.Should().ThrowAsync<NotFoundException>();
    }
}
