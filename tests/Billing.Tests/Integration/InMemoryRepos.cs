using System.Collections.Concurrent;
using Billing.Application.Common;
using Billing.Domain.Common;
using Billing.Domain.Orcamentos;
using Billing.Domain.Pagamentos;

namespace Billing.Tests.Integration;

/// <summary>Reproduz o contrato dos repositórios Mongo: índice único por OS e gravação otimista por Versao.</summary>
public sealed class InMemoryOrcamentoRepository : IOrcamentoRepository
{
    public ConcurrentDictionary<Guid, Orcamento> Itens { get; } = new();

    public Task<Orcamento?> ObterPorIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Itens.GetValueOrDefault(id));

    public Task<Orcamento?> ObterPorOsAsync(Guid osId, CancellationToken ct = default)
        => Task.FromResult(Itens.Values.Where(o => o.OrdemServicoId == osId).OrderByDescending(o => o.CriadoEm).FirstOrDefault());

    public Task<IReadOnlyList<Orcamento>> ListarPorOsAsync(Guid osId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Orcamento>>(Itens.Values.Where(o => o.OrdemServicoId == osId).OrderByDescending(o => o.CriadoEm).ToList());

    public Task InserirAsync(Orcamento o, CancellationToken ct = default)
    {
        if (Itens.Values.Any(x => x.OrdemServicoId == o.OrdemServicoId)) throw new DuplicidadeException("orcamentos.OrdemServicoId");
        Itens[o.Id] = o;
        return Task.CompletedTask;
    }

    public Task AtualizarAsync(Orcamento o, CancellationToken ct = default)
    {
        o.AvancarVersao();
        Itens[o.Id] = o;
        return Task.CompletedTask;
    }
}

public sealed class InMemoryPagamentoRepository : IPagamentoRepository
{
    public ConcurrentDictionary<Guid, Pagamento> Itens { get; } = new();

    public Task<Pagamento?> ObterPorIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Itens.GetValueOrDefault(id));

    public Task<Pagamento?> ObterPorOsAsync(Guid osId, CancellationToken ct = default)
        => Task.FromResult(Itens.Values.FirstOrDefault(p => p.OrdemServicoId == osId));

    public Task InserirAsync(Pagamento p, CancellationToken ct = default)
    {
        if (Itens.Values.Any(x => x.OrdemServicoId == p.OrdemServicoId)) throw new DuplicidadeException("pagamentos.OrdemServicoId");
        Itens[p.Id] = p;
        return Task.CompletedTask;
    }

    public Task AtualizarAsync(Pagamento p, CancellationToken ct = default)
    {
        p.AvancarVersao();
        Itens[p.Id] = p;
        return Task.CompletedTask;
    }
}

public sealed class NoOpUnitOfWork : IUnitOfWork
{
    public Task<int> CommitAsync(CancellationToken ct = default) => Task.FromResult(0);
}