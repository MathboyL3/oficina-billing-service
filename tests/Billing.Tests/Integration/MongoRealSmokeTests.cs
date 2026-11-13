using Billing.Application.Common;
using Billing.Domain.Orcamentos;
using Billing.Domain.Pagamentos;
using Billing.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Billing.Tests.Integration;

/// <summary>
/// Fumaça contra um MongoDB real (replica set). Só roda com <c>BILLING_MONGO_TEST</c> definida
/// (ex.: mongodb://localhost:27099/?replicaSet=rs0&amp;directConnection=true); sem ela passa sem exercitar nada,
/// porque o CI de testes não tem Mongo.
/// </summary>
public class MongoRealSmokeTests
{
    private static readonly string? Conn = Environment.GetEnvironmentVariable("BILLING_MONGO_TEST");

    private (OrcamentoRepository Orc, PagamentoRepository Pag) Repos()
    {
        MongoMapeamentos.Registrar();
        var client = new MongoClient(Conn);
        var db = new MongoContext(client, Options.Create(new MongoOptions { Database = "billing_smoke_" + Guid.NewGuid().ToString("N")[..8] }));
        var sp = new ServiceCollection().BuildServiceProvider();
        var sessao = new MongoSessao(sp);
        return (new OrcamentoRepository(db, sessao), new PagamentoRepository(db, sessao));
    }

    [Fact]
    public async Task Orcamento_ida_e_volta_indice_unico_e_versao()
    {
        if (Conn is null) return;
        var (orc, _) = Repos();
        var o = Orcamento.Gerar(Guid.NewGuid(), Guid.NewGuid(),
            new[] { new ItemOrcamentoDominio("Peca", Guid.NewGuid(), "Filtro", 25.5m, 2) }, TimeSpan.FromHours(72));

        await orc.InserirAsync(o);
        var lido = await orc.ObterPorIdAsync(o.Id);

        lido!.ValorTotal.Should().Be(51m);
        lido.Itens.Should().ContainSingle().Which.Subtotal.Should().Be(51m);
        lido.Status.Should().Be(StatusOrcamento.Pendente);
        (await orc.ObterPorOsAsync(o.OrdemServicoId))!.Id.Should().Be(o.Id);

        var outro = Orcamento.Gerar(o.OrdemServicoId, o.ClienteId, new[] { new ItemOrcamentoDominio("Peca", Guid.NewGuid(), "x", 1m, 1) }, TimeSpan.FromHours(1));
        var dup = async () => await orc.InserirAsync(outro);
        await dup.Should().ThrowAsync<DuplicidadeException>();

        var a = (await orc.ObterPorIdAsync(o.Id))!;
        var b = (await orc.ObterPorIdAsync(o.Id))!;
        a.Aprovar(DateTimeOffset.UtcNow);
        await orc.AtualizarAsync(a);
        b.Recusar("x");
        var perdeu = async () => await orc.AtualizarAsync(b);
        await perdeu.Should().ThrowAsync<ConcorrenciaException>();
        (await orc.ObterPorIdAsync(o.Id))!.Versao.Should().Be(1);
    }

    [Fact]
    public async Task Pagamento_ida_e_volta_indice_unico_e_versao()
    {
        if (Conn is null) return;
        var (_, pag) = Repos();
        var p = Pagamento.Criar(Guid.NewGuid(), Guid.NewGuid(), 151m);
        p.RegistrarPreferencia("pref-1", "https://mp/checkout");

        await pag.InserirAsync(p);
        var lido = (await pag.ObterPorOsAsync(p.OrdemServicoId))!;

        lido.Valor.Should().Be(151m);
        lido.PreferenceId.Should().Be("pref-1");
        lido.Status.Should().Be(StatusPagamento.Pendente);

        var dup = async () => await pag.InserirAsync(Pagamento.Criar(p.OrdemServicoId, Guid.NewGuid(), 1m));
        await dup.Should().ThrowAsync<DuplicidadeException>();

        var a = (await pag.ObterPorIdAsync(p.Id))!;
        var b = (await pag.ObterPorIdAsync(p.Id))!;
        a.Aprovar("9");
        await pag.AtualizarAsync(a);
        b.Recusar("9");
        var perdeu = async () => await pag.AtualizarAsync(b);
        await perdeu.Should().ThrowAsync<ConcorrenciaException>();
        (await pag.ObterPorIdAsync(p.Id))!.Status.Should().Be(StatusPagamento.Aprovado);
    }
}