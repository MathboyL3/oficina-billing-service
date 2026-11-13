using Billing.Application;
using Billing.Application.Consumers;
using Billing.Application.Pagamentos;
using Billing.Application.UseCases;
using Billing.Domain.Common;
using Billing.Domain.Orcamentos;
using Billing.Domain.Pagamentos;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Options;
using Moq;
using Oficina.Contracts;

namespace Billing.Tests.Application;

public class BillingConsumersTests
{
    private readonly Mock<IOrcamentoRepository> _orcs = new();
    private readonly Mock<IPagamentoRepository> _pags = new();
    private readonly Mock<IGatewayPagamento> _gw = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly List<string> _ordem = new();

    public BillingConsumersTests()
    {
        _uow.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Callback(() => _ordem.Add("commit")).ReturnsAsync(1);
    }

    private Mock<ConsumeContext<T>> Ctx<T>(T msg) where T : class
    {
        var ctx = new Mock<ConsumeContext<T>>();
        ctx.SetupGet(c => c.Message).Returns(msg);
        ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        ctx.Setup(c => c.Publish(It.IsAny<It.IsAnyType>(), It.IsAny<CancellationToken>()))
            .Callback((object m, CancellationToken _) => _ordem.Add(m.GetType().Name)).Returns(Task.CompletedTask);
        return ctx;
    }

    private static Orcamento NovoOrc(Guid os) =>
        Orcamento.Gerar(os, Guid.NewGuid(), new[] { new ItemOrcamentoDominio("Servico", Guid.NewGuid(), "x", 10m, 1) }, TimeSpan.FromHours(1));

    private GerarOrcamentoConsumer Gerar() => new(_orcs.Object, _uow.Object, Options.Create(new BillingOptions()), Microsoft.Extensions.Logging.Abstractions.NullLogger<GerarOrcamentoConsumer>.Instance);
    private CriarCobrancaConsumer Cobrar(string modo) =>
        new(_pags.Object, _gw.Object, new WebhookMercadoPagoUseCase(_gw.Object, _pags.Object, new Mock<IPublishEndpoint>().Object), _uow.Object, Options.Create(new ModoPagamento { Modo = modo }));
    private CancelarOrcamentoConsumer Cancelar() => new(_orcs.Object, _pags.Object, _gw.Object, _uow.Object);
    private EstornarPagamentoConsumer Estornar() => new(_pags.Object, _gw.Object, _uow.Object);

    [Fact]
    public async Task GerarOrcamento_cria_e_publica_OrcamentoGerado()
    {
        var os = Guid.NewGuid();
        var ctx = Ctx(new GerarOrcamento(os, DateTime.UtcNow, Guid.NewGuid(),
            new[] { new ItemOrcamento("Peca", Guid.NewGuid(), "p", 5m, 2), new ItemOrcamento("Servico", Guid.NewGuid(), "s", 10m, 1) }, 20m));

        await Gerar().Consume(ctx.Object);

        _orcs.Verify(r => r.InserirAsync(It.Is<Orcamento>(o => o.ValorTotal == 20m && o.OrdemServicoId == os), It.IsAny<CancellationToken>()), Times.Once);
        _ordem.Should().Equal("OrcamentoGerado", "commit");
    }

    [Fact]
    public async Task GerarOrcamento_duplicado_republica_sem_inserir()
    {
        var os = Guid.NewGuid();
        _orcs.Setup(r => r.ObterPorOsAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(NovoOrc(os));

        await Gerar().Consume(Ctx(new GerarOrcamento(os, DateTime.UtcNow, Guid.NewGuid(), new[] { new ItemOrcamento("Peca", Guid.NewGuid(), "p", 5m, 2) }, 10m)).Object);

        _orcs.Verify(r => r.InserirAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>()), Times.Never);
        _ordem.Should().Equal("OrcamentoGerado", "commit");
    }

    [Fact]
    public async Task GerarOrcamento_sem_itens_publica_GeracaoOrcamentoFalhou()
    {
        var ctx = Ctx(new GerarOrcamento(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), Array.Empty<ItemOrcamento>(), 0m));
        await Gerar().Consume(ctx.Object);
        _orcs.Verify(r => r.InserirAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>()), Times.Never);
        ctx.Verify(c => c.Publish(It.Is<GeracaoOrcamentoFalhou>(m => m.Motivo.Length > 0), It.IsAny<CancellationToken>()), Times.Once);
        _ordem.Should().Equal("GeracaoOrcamentoFalhou", "commit");
    }

    [Fact]
    public async Task GerarOrcamento_itens_nulos_publica_falha_sem_excecao()
    {
        var ctx = Ctx(new GerarOrcamento(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), null!, 0m));
        await Gerar().Consume(ctx.Object);
        _ordem.Should().Equal("GeracaoOrcamentoFalhou", "commit");
    }

    [Fact]
    public async Task GerarOrcamento_reentrega_com_orcamento_nao_pendente_nao_gera_nem_publica()
    {
        var os = Guid.NewGuid();
        var orc = NovoOrc(os);
        orc.Cancelar();
        _orcs.Setup(r => r.ObterPorOsAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(orc);

        await Gerar().Consume(Ctx(new GerarOrcamento(os, DateTime.UtcNow, Guid.NewGuid(), new[] { new ItemOrcamento("Peca", Guid.NewGuid(), "p", 5m, 2) }, 10m)).Object);

        _orcs.Verify(r => r.InserirAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>()), Times.Never);
        _ordem.Should().BeEmpty();
    }

    [Fact]
    public async Task GerarOrcamento_item_sem_descricao_publica_falha_sem_inserir()
    {
        var ctx = Ctx(new GerarOrcamento(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), new[] { new ItemOrcamento("Peca", Guid.NewGuid(), " ", 5m, 1) }, 5m));
        await Gerar().Consume(ctx.Object);
        _orcs.Verify(r => r.InserirAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>()), Times.Never);
        ctx.Verify(c => c.Publish(It.Is<GeracaoOrcamentoFalhou>(m => m.Motivo.Contains("descrição")), It.IsAny<CancellationToken>()), Times.Once);
        _ordem.Should().Equal("GeracaoOrcamentoFalhou", "commit");
    }

    [Fact]
    public async Task CancelarOrcamento_pagamento_Aprovado_no_banco_estorna_antes_de_cancelar()
    {
        var os = Guid.NewGuid();
        var orc = NovoOrc(os);
        var pag = Pagamento.Criar(os, orc.Id, 10m);
        pag.Aprovar("66");
        _orcs.Setup(r => r.ObterPorOsAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(orc);
        _pags.Setup(r => r.ObterPorOsAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(pag);
        _gw.Setup(g => g.EstornarAsync("66", null, It.IsAny<CancellationToken>())).ReturnsAsync(new EstornoResult(true, null));

        await Cancelar().Consume(Ctx(new CancelarOrcamento(os, DateTime.UtcNow, "x")).Object);

        pag.Status.Should().Be(StatusPagamento.Estornado);
        orc.Status.Should().Be(StatusOrcamento.Cancelado);
        _ordem.Should().Equal("PagamentoEstornado", "OrcamentoCancelado", "commit");
    }

    [Fact]
    public async Task CancelarOrcamento_pagamento_EstornoPendente_com_falha_publica_EstornoFalhou_e_ainda_cancela()
    {
        var os = Guid.NewGuid();
        var orc = NovoOrc(os);
        var pag = Pagamento.Criar(os, orc.Id, 10m);
        pag.Aprovar("66");
        pag.MarcarEstornoPendente();
        _orcs.Setup(r => r.ObterPorOsAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(orc);
        _pags.Setup(r => r.ObterPorOsAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(pag);
        _gw.Setup(g => g.EstornarAsync("66", null, It.IsAny<CancellationToken>())).ReturnsAsync(new EstornoResult(false, "boom"));
        _gw.Setup(g => g.ObterPagamentoAsync("66", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagamentoInfo("66", StatusPagamentoGateway.Aprovado, pag.Id.ToString(), "approved"));

        await Cancelar().Consume(Ctx(new CancelarOrcamento(os, DateTime.UtcNow, "x")).Object);

        pag.Status.Should().Be(StatusPagamento.EstornoPendente);
        orc.Status.Should().Be(StatusOrcamento.Cancelado);
        _ordem.Should().Equal("EstornoFalhou", "OrcamentoCancelado", "commit");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task Estornar_sem_PaymentIdMp_publica_EstornoFalhou_sem_chamar_gateway(string? paymentIdMp)
    {
        var os = Guid.NewGuid();
        var pag = Pagamento.Criar(os, Guid.NewGuid(), 10m);
        pag.Aprovar(paymentIdMp!);
        _pags.Setup(r => r.ObterPorIdAsync(pag.Id, It.IsAny<CancellationToken>())).ReturnsAsync(pag);

        await Estornar().Consume(Ctx(new EstornarPagamento(os, DateTime.UtcNow, pag.Id, "x")).Object);

        _gw.VerifyNoOtherCalls();
        pag.Status.Should().Be(StatusPagamento.EstornoPendente);
        _ordem.Should().Equal("EstornoFalhou", "commit");
    }
    [Fact]
    public async Task CriarCobranca_real_cria_pagamento_e_publica_CobrancaCriada_sem_confirmar()
    {
        var os = Guid.NewGuid();
        Pagamento? inserido = null;
        _gw.Setup(g => g.CriarCobrancaAsync(It.IsAny<CriarCobrancaRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new CobrancaCriadaResult("pref-1", "http://mp/pay"));
        _pags.Setup(r => r.InserirAsync(It.IsAny<Pagamento>(), It.IsAny<CancellationToken>())).Callback((Pagamento p, CancellationToken _) => inserido = p).Returns(Task.CompletedTask);

        await Cobrar("Real").Consume(Ctx(new CriarCobranca(os, DateTime.UtcNow, Guid.NewGuid(), 10m)).Object);

        inserido!.PreferenceId.Should().Be("pref-1");
        inserido.Status.Should().Be(StatusPagamento.Pendente);
        _gw.Verify(g => g.BuscarPorReferenciaAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _ordem.Should().Equal("CobrancaCriada", "commit");
    }

    [Fact]
    public async Task CriarCobranca_reentregue_com_preferencia_nao_chama_gateway_e_republica()
    {
        var os = Guid.NewGuid();
        var pag = Pagamento.Criar(os, Guid.NewGuid(), 10m);
        pag.RegistrarPreferencia("pref-1", "http://mp/pay");
        _pags.Setup(r => r.ObterPorOsAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(pag);
        var ctx = Ctx(new CriarCobranca(os, DateTime.UtcNow, pag.OrcamentoId, 10m));

        await Cobrar("Real").Consume(ctx.Object);

        _gw.Verify(g => g.CriarCobrancaAsync(It.IsAny<CriarCobrancaRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        ctx.Verify(c => c.Publish(It.Is<CobrancaCriada>(m => m.LinkPagamento == "http://mp/pay" && m.PagamentoId == pag.Id), It.IsAny<CancellationToken>()), Times.Once);
        _ordem.Should().Equal("CobrancaCriada", "commit");
    }

    [Fact]
    public async Task CriarCobranca_fake_confirma_na_hora()
    {
        var os = Guid.NewGuid();
        Pagamento? inserido = null;
        _gw.Setup(g => g.CriarCobrancaAsync(It.IsAny<CriarCobrancaRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new CobrancaCriadaResult("pref-1", "http://fake/pay"));
        _pags.Setup(r => r.InserirAsync(It.IsAny<Pagamento>(), It.IsAny<CancellationToken>())).Callback((Pagamento p, CancellationToken _) => inserido = p).Returns(Task.CompletedTask);
        _pags.Setup(r => r.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => inserido);
        _gw.Setup(g => g.BuscarPorReferenciaAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string r, CancellationToken _) => new PagamentoInfo("9", StatusPagamentoGateway.Aprovado, r, "approved"));
        var bus = new Mock<IPublishEndpoint>();
        bus.Setup(b => b.Publish(It.IsAny<PagamentoConfirmado>(), It.IsAny<CancellationToken>())).Callback(() => _ordem.Add("PagamentoConfirmado")).Returns(Task.CompletedTask);
        var c = new CriarCobrancaConsumer(_pags.Object, _gw.Object, new WebhookMercadoPagoUseCase(_gw.Object, _pags.Object, bus.Object), _uow.Object, Options.Create(new ModoPagamento { Modo = "Fake" }));

        await c.Consume(Ctx(new CriarCobranca(os, DateTime.UtcNow, Guid.NewGuid(), 10m)).Object);

        inserido!.Status.Should().Be(StatusPagamento.Aprovado);
        _ordem.Should().Equal("CobrancaCriada", "PagamentoConfirmado", "commit");
    }

    [Fact]
    public async Task CancelarOrcamento_sem_pagamento_cancela_e_confirma()
    {
        var os = Guid.NewGuid();
        var orc = NovoOrc(os);
        _orcs.Setup(r => r.ObterPorOsAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(orc);
        var ctx = Ctx(new CancelarOrcamento(os, DateTime.UtcNow, "x"));

        await Cancelar().Consume(ctx.Object);

        orc.Status.Should().Be(StatusOrcamento.Cancelado);
        _orcs.Verify(r => r.AtualizarAsync(orc, It.IsAny<CancellationToken>()), Times.Once);
        ctx.Verify(c => c.Publish(It.Is<OrcamentoCancelado>(m => m.OrcamentoId == orc.Id), It.IsAny<CancellationToken>()), Times.Once);
        _ordem.Should().Equal("OrcamentoCancelado", "commit");
    }

    [Fact]
    public async Task CancelarOrcamento_com_pagamento_aprovado_no_MP_estorna_antes()
    {
        var os = Guid.NewGuid();
        var orc = NovoOrc(os);
        var pag = Pagamento.Criar(os, orc.Id, 10m);
        _orcs.Setup(r => r.ObterPorOsAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(orc);
        _pags.Setup(r => r.ObterPorOsAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(pag);
        _gw.Setup(g => g.BuscarPorReferenciaAsync(pag.Id.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagamentoInfo("55", StatusPagamentoGateway.Aprovado, pag.Id.ToString(), "approved"));
        _gw.Setup(g => g.EstornarAsync("55", null, It.IsAny<CancellationToken>())).ReturnsAsync(new EstornoResult(true, null));

        await Cancelar().Consume(Ctx(new CancelarOrcamento(os, DateTime.UtcNow, "recusado")).Object);

        orc.Status.Should().Be(StatusOrcamento.Cancelado);
        pag.Status.Should().Be(StatusPagamento.Estornado);
        _ordem.Should().Equal("PagamentoEstornado", "OrcamentoCancelado", "commit");
    }

    [Fact]
    public async Task CancelarOrcamento_com_pagamento_aprovado_e_estorno_falho_publica_EstornoFalhou()
    {
        var os = Guid.NewGuid();
        var pag = Pagamento.Criar(os, Guid.NewGuid(), 10m);
        _pags.Setup(r => r.ObterPorOsAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(pag);
        _gw.Setup(g => g.BuscarPorReferenciaAsync(pag.Id.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagamentoInfo("55", StatusPagamentoGateway.Aprovado, pag.Id.ToString(), "approved"));
        _gw.Setup(g => g.EstornarAsync("55", null, It.IsAny<CancellationToken>())).ReturnsAsync(new EstornoResult(false, "boom"));
        _gw.Setup(g => g.ObterPagamentoAsync("55", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagamentoInfo("55", StatusPagamentoGateway.Aprovado, pag.Id.ToString(), "approved"));

        await Cancelar().Consume(Ctx(new CancelarOrcamento(os, DateTime.UtcNow, "x")).Object);

        pag.Status.Should().Be(StatusPagamento.EstornoPendente);
        _ordem.Should().Equal("EstornoFalhou", "OrcamentoCancelado", "commit");
    }

    [Fact]
    public async Task CancelarOrcamento_pagamento_pendente_no_MP_nao_estorna()
    {
        var os = Guid.NewGuid();
        var pag = Pagamento.Criar(os, Guid.NewGuid(), 10m);
        _pags.Setup(r => r.ObterPorOsAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(pag);
        _gw.Setup(g => g.BuscarPorReferenciaAsync(pag.Id.ToString(), It.IsAny<CancellationToken>())).ReturnsAsync((PagamentoInfo?)null);

        await Cancelar().Consume(Ctx(new CancelarOrcamento(os, DateTime.UtcNow, "x")).Object);

        _gw.Verify(g => g.EstornarAsync(It.IsAny<string>(), It.IsAny<decimal?>(), It.IsAny<CancellationToken>()), Times.Never);
        _ordem.Should().Equal("OrcamentoCancelado", "commit");
    }

    [Fact]
    public async Task CancelarOrcamento_sem_orcamento_ainda_confirma_com_null()
    {
        var os = Guid.NewGuid();
        var ctx = Ctx(new CancelarOrcamento(os, DateTime.UtcNow, "x"));
        await Cancelar().Consume(ctx.Object);
        ctx.Verify(c => c.Publish(It.Is<OrcamentoCancelado>(m => m.OrcamentoId == null), It.IsAny<CancellationToken>()), Times.Once);
        _ordem.Should().Equal("OrcamentoCancelado", "commit");
    }

    private Pagamento Aprovado(Guid os)
    {
        var pag = Pagamento.Criar(os, Guid.NewGuid(), 10m);
        pag.Aprovar("77");
        _pags.Setup(r => r.ObterPorIdAsync(pag.Id, It.IsAny<CancellationToken>())).ReturnsAsync(pag);
        return pag;
    }

    [Fact]
    public async Task Estornar_aprovado_sucesso()
    {
        var os = Guid.NewGuid();
        var pag = Aprovado(os);
        _gw.Setup(g => g.EstornarAsync("77", null, It.IsAny<CancellationToken>())).ReturnsAsync(new EstornoResult(true, null));

        await Estornar().Consume(Ctx(new EstornarPagamento(os, DateTime.UtcNow, pag.Id, "x")).Object);

        pag.Status.Should().Be(StatusPagamento.Estornado);
        _pags.Verify(r => r.AtualizarAsync(pag, It.IsAny<CancellationToken>()), Times.Once);
        _ordem.Should().Equal("PagamentoEstornado", "commit");
    }

    [Fact]
    public async Task Estornar_falha_marca_pendente_e_publica_EstornoFalhou()
    {
        var os = Guid.NewGuid();
        var pag = Aprovado(os);
        _gw.Setup(g => g.EstornarAsync("77", null, It.IsAny<CancellationToken>())).ReturnsAsync(new EstornoResult(false, "indisponivel"));
        _gw.Setup(g => g.ObterPagamentoAsync("77", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagamentoInfo("77", StatusPagamentoGateway.Aprovado, pag.Id.ToString(), "approved"));

        await Estornar().Consume(Ctx(new EstornarPagamento(os, DateTime.UtcNow, pag.Id, "x")).Object);

        pag.Status.Should().Be(StatusPagamento.EstornoPendente);
        _ordem.Should().Equal("EstornoFalhou", "commit");
    }

    [Fact]
    public async Task Estornar_falha_mas_MP_ja_estornado_trata_como_sucesso()
    {
        var os = Guid.NewGuid();
        var pag = Aprovado(os);
        _gw.Setup(g => g.EstornarAsync("77", null, It.IsAny<CancellationToken>())).ReturnsAsync(new EstornoResult(false, "timeout"));
        _gw.Setup(g => g.ObterPagamentoAsync("77", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagamentoInfo("77", StatusPagamentoGateway.Estornado, pag.Id.ToString(), "refunded"));

        await Estornar().Consume(Ctx(new EstornarPagamento(os, DateTime.UtcNow, pag.Id, "x")).Object);

        pag.Status.Should().Be(StatusPagamento.Estornado);
        _ordem.Should().Equal("PagamentoEstornado", "commit");
    }

    [Fact]
    public async Task Estornar_falha_e_reconsulta_lanca_mantem_falha()
    {
        var os = Guid.NewGuid();
        var pag = Aprovado(os);
        _gw.Setup(g => g.EstornarAsync("77", null, It.IsAny<CancellationToken>())).ReturnsAsync(new EstornoResult(false, "x"));
        _gw.Setup(g => g.ObterPagamentoAsync("77", It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("404"));

        await Estornar().Consume(Ctx(new EstornarPagamento(os, DateTime.UtcNow, pag.Id, "x")).Object);

        pag.Status.Should().Be(StatusPagamento.EstornoPendente);
        _ordem.Should().Equal("EstornoFalhou", "commit");
    }

    [Fact]
    public async Task Estornar_em_EstornoPendente_reprocessa()
    {
        var os = Guid.NewGuid();
        var pag = Aprovado(os);
        pag.MarcarEstornoPendente();
        _gw.Setup(g => g.EstornarAsync("77", null, It.IsAny<CancellationToken>())).ReturnsAsync(new EstornoResult(true, null));

        await Estornar().Consume(Ctx(new EstornarPagamento(os, DateTime.UtcNow, pag.Id, "x")).Object);

        pag.Status.Should().Be(StatusPagamento.Estornado);
    }

    [Fact]
    public async Task Estornar_pendente_aprovado_no_MP_estorna()
    {
        var os = Guid.NewGuid();
        var pag = Pagamento.Criar(os, Guid.NewGuid(), 10m);
        _pags.Setup(r => r.ObterPorIdAsync(pag.Id, It.IsAny<CancellationToken>())).ReturnsAsync(pag);
        _gw.Setup(g => g.BuscarPorReferenciaAsync(pag.Id.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagamentoInfo("88", StatusPagamentoGateway.Aprovado, pag.Id.ToString(), "approved"));
        _gw.Setup(g => g.EstornarAsync("88", null, It.IsAny<CancellationToken>())).ReturnsAsync(new EstornoResult(true, null));

        await Estornar().Consume(Ctx(new EstornarPagamento(os, DateTime.UtcNow, pag.Id, "x")).Object);

        pag.Status.Should().Be(StatusPagamento.Estornado);
        _ordem.Should().Equal("PagamentoEstornado", "commit");
    }

    [Fact]
    public async Task Estornar_pendente_nao_pago_no_MP_confirma_sem_estornar()
    {
        var os = Guid.NewGuid();
        var pag = Pagamento.Criar(os, Guid.NewGuid(), 10m);
        _pags.Setup(r => r.ObterPorIdAsync(pag.Id, It.IsAny<CancellationToken>())).ReturnsAsync(pag);
        _gw.Setup(g => g.BuscarPorReferenciaAsync(pag.Id.ToString(), It.IsAny<CancellationToken>())).ReturnsAsync((PagamentoInfo?)null);

        await Estornar().Consume(Ctx(new EstornarPagamento(os, DateTime.UtcNow, pag.Id, "x")).Object);

        _gw.Verify(g => g.EstornarAsync(It.IsAny<string>(), It.IsAny<decimal?>(), It.IsAny<CancellationToken>()), Times.Never);
        _ordem.Should().Equal("PagamentoEstornado", "commit");
    }

    [Fact]
    public async Task Estornar_ja_estornado_ou_inexistente_confirma()
    {
        var os = Guid.NewGuid();
        var pag = Aprovado(os);
        pag.MarcarEstornado();

        await Estornar().Consume(Ctx(new EstornarPagamento(os, DateTime.UtcNow, pag.Id, "x")).Object);
        await Estornar().Consume(Ctx(new EstornarPagamento(os, DateTime.UtcNow, Guid.NewGuid(), "x")).Object);

        _gw.VerifyNoOtherCalls();
        _ordem.Should().Equal("PagamentoEstornado", "commit", "PagamentoEstornado", "commit");
    }

    [Fact]
    public async Task Estornar_recusado_confirma_sem_chamar_gateway()
    {
        var os = Guid.NewGuid();
        var pag = Pagamento.Criar(os, Guid.NewGuid(), 10m);
        pag.Recusar("1");
        _pags.Setup(r => r.ObterPorIdAsync(pag.Id, It.IsAny<CancellationToken>())).ReturnsAsync(pag);

        await Estornar().Consume(Ctx(new EstornarPagamento(os, DateTime.UtcNow, pag.Id, "x")).Object);

        _gw.VerifyNoOtherCalls();
        _ordem.Should().Equal("PagamentoEstornado", "commit");
    }
}

