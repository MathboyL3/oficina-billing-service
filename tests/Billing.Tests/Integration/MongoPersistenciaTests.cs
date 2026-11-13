using Billing.Application.Common;
using Billing.Domain.Common;
using Billing.Domain.Orcamentos;
using Billing.Domain.Pagamentos;
using Billing.Infrastructure.Persistence;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Billing.Tests.Integration;

/// <summary>Repositórios e unit of work contra MongoDB real (replica set) — sem mocks do driver.</summary>
[Collection("mongo")]
public class MongoPersistenciaTests
{
    private readonly MongoFixture _mongo;
    public MongoPersistenciaTests(MongoFixture mongo) => _mongo = mongo;

    private static Orcamento NovoOrc(Guid? os = null) =>
        Orcamento.Gerar(os ?? Guid.NewGuid(), Guid.NewGuid(),
            new[] { new ItemOrcamentoDominio("Peca", Guid.NewGuid(), "Filtro", 25.5m, 2) }, TimeSpan.FromHours(72));

    private (OrcamentoRepository Orc, PagamentoRepository Pag, MongoContext Db) Repos()
    {
        var db = _mongo.NovoContexto(out _);
        var sessao = new MongoSessao(new ServiceCollection().BuildServiceProvider());
        return (new OrcamentoRepository(db, sessao), new PagamentoRepository(db, sessao), db);
    }

    [Fact]
    public async Task Orcamento_ida_e_volta_preserva_itens_total_e_status()
    {
        var (repo, _, _) = Repos();
        var o = NovoOrc();

        await repo.InserirAsync(o);
        var lido = (await repo.ObterPorIdAsync(o.Id))!;

        lido.ValorTotal.Should().Be(51m);
        lido.ClienteId.Should().Be(o.ClienteId);
        lido.Status.Should().Be(StatusOrcamento.Pendente);
        lido.Itens.Should().ContainSingle().Which.Subtotal.Should().Be(51m);
        (await repo.ObterPorOsAsync(o.OrdemServicoId))!.Id.Should().Be(o.Id);
        (await repo.ListarPorOsAsync(o.OrdemServicoId)).Should().ContainSingle();
    }

    [Fact]
    public async Task Pagamento_ida_e_volta_preserva_preferencia_e_status()
    {
        var (_, repo, _) = Repos();
        var p = Pagamento.Criar(Guid.NewGuid(), Guid.NewGuid(), 151m);
        p.RegistrarPreferencia("pref-1", "https://mp/checkout");

        await repo.InserirAsync(p);
        var lido = (await repo.ObterPorOsAsync(p.OrdemServicoId))!;

        lido.Valor.Should().Be(151m);
        lido.PreferenceId.Should().Be("pref-1");
        lido.LinkPagamento.Should().Be("https://mp/checkout");
        lido.Status.Should().Be(StatusPagamento.Pendente);
        (await repo.ObterPorIdAsync(p.Id))!.OrcamentoId.Should().Be(p.OrcamentoId);
    }

    [Fact]
    public async Task Indice_unico_por_OS_gera_DuplicidadeException_em_orcamento_e_pagamento()
    {
        var (orc, pag, _) = Repos();
        var o = NovoOrc();
        await orc.InserirAsync(o);
        var p = Pagamento.Criar(o.OrdemServicoId, o.Id, 10m);
        await pag.InserirAsync(p);

        var dupO = async () => await orc.InserirAsync(NovoOrc(o.OrdemServicoId));
        var dupP = async () => await pag.InserirAsync(Pagamento.Criar(o.OrdemServicoId, Guid.NewGuid(), 1m));

        await dupO.Should().ThrowAsync<DuplicidadeException>();
        await dupP.Should().ThrowAsync<DuplicidadeException>();
    }

    [Fact]
    public async Task Orcamento_ReplaceOne_com_versao_desatualizada_lanca_ConcorrenciaException_e_nao_altera()
    {
        var (repo, _, _) = Repos();
        var o = NovoOrc();
        await repo.InserirAsync(o);
        var a = (await repo.ObterPorIdAsync(o.Id))!;
        var b = (await repo.ObterPorIdAsync(o.Id))!;
        a.Aprovar(DateTimeOffset.UtcNow);
        await repo.AtualizarAsync(a);
        b.Recusar("x");

        var perdeu = async () => await repo.AtualizarAsync(b);

        await perdeu.Should().ThrowAsync<ConcorrenciaException>();
        b.Versao.Should().Be(0, "a versão só avança quando a gravação acontece");
        var final = (await repo.ObterPorIdAsync(o.Id))!;
        final.Status.Should().Be(StatusOrcamento.Aprovado);
        final.Versao.Should().Be(1);
    }

    [Fact]
    public async Task Pagamento_ReplaceOne_com_versao_desatualizada_lanca_ConcorrenciaException_e_nao_altera()
    {
        var (_, repo, _) = Repos();
        var p = Pagamento.Criar(Guid.NewGuid(), Guid.NewGuid(), 10m);
        await repo.InserirAsync(p);
        var a = (await repo.ObterPorIdAsync(p.Id))!;
        var b = (await repo.ObterPorIdAsync(p.Id))!;
        a.Aprovar("9");
        await repo.AtualizarAsync(a);
        b.Recusar("9");

        var perdeu = async () => await repo.AtualizarAsync(b);

        await perdeu.Should().ThrowAsync<ConcorrenciaException>();
        b.Versao.Should().Be(0);
        (await repo.ObterPorIdAsync(p.Id))!.Status.Should().Be(StatusPagamento.Aprovado);
    }

    private ServiceProvider ProvedorComOutbox(string banco)
    {
        MongoMapeamentos.Registrar();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IMongoClient>(_ => new MongoClient(_mongo.ConnectionString));
        services.AddSingleton(sp => new MongoContext(sp.GetRequiredService<IMongoClient>(), Options.Create(new MongoOptions { Database = banco })));
        services.AddScoped<MongoSessao>();
        services.AddScoped<IUnitOfWork, MongoUnitOfWork>();
        services.AddScoped<IOrcamentoRepository, OrcamentoRepository>();
        services.AddMassTransit(x =>
        {
            x.AddMongoDbOutbox(o =>
            {
                o.ClientFactory(_ => new MongoClient(_mongo.ConnectionString));
                o.DatabaseFactory(_ => new MongoClient(_mongo.ConnectionString).GetDatabase(banco));
                o.UseBusOutbox(b => b.DisableDeliveryService());
            });
            x.UsingInMemory();
        });
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task UnitOfWork_insert_seguido_de_falha_sem_commit_nao_persiste_nada()
    {
        var banco = "billing_uow_" + Guid.NewGuid().ToString("N")[..8];
        await using var sp = ProvedorComOutbox(banco);
        var o = NovoOrc();

        var falha = async () =>
        {
            using var escopo = sp.CreateScope();
            var repo = escopo.ServiceProvider.GetRequiredService<IOrcamentoRepository>();
            await repo.InserirAsync(o);
            (await repo.ObterPorIdAsync(o.Id)).Should().NotBeNull("dentro da transação a leitura enxerga a escrita");
            throw new InvalidOperationException("falha antes do commit");
        };
        await falha.Should().ThrowAsync<InvalidOperationException>();

        var db = new MongoClient(_mongo.ConnectionString).GetDatabase(banco);
        (await db.GetCollection<Orcamento>("orcamentos").CountDocumentsAsync(x => x.Id == o.Id)).Should().Be(0);
    }

    [Fact]
    public async Task UnitOfWork_commit_persiste_a_escrita_da_transacao()
    {
        var banco = "billing_uow_" + Guid.NewGuid().ToString("N")[..8];
        await using var sp = ProvedorComOutbox(banco);
        var o = NovoOrc();

        using (var escopo = sp.CreateScope())
        {
            await escopo.ServiceProvider.GetRequiredService<IOrcamentoRepository>().InserirAsync(o);
            (await escopo.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync()).Should().Be(1);
        }

        var db = new MongoClient(_mongo.ConnectionString).GetDatabase(banco);
        (await db.GetCollection<Orcamento>("orcamentos").CountDocumentsAsync(x => x.Id == o.Id)).Should().Be(1);
    }

    [Fact]
    public async Task UnitOfWork_sem_outbox_nao_abre_transacao_e_commit_e_noop()
    {
        var sessao = new MongoSessao(new ServiceCollection().BuildServiceProvider());
        sessao.Atual.Should().BeNull();
        (await sessao.ParaEscritaAsync(default)).Should().BeNull();
        (await new MongoUnitOfWork(sessao).CommitAsync()).Should().Be(0);
    }
}