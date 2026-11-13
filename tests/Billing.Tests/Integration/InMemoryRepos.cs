using System.Collections.Concurrent;
using Billing.Application.Common;
using Billing.Domain.Common;
using Billing.Domain.Orcamentos;
using Billing.Domain.Pagamentos;

namespace Billing.Tests.Integration;

/// <summary>
/// Aproxima os repositórios Mongo para os testes HTTP: índice único por OS e gravação otimista por Versao
/// (versão gravada rastreada à parte; as instâncias são compartilhadas). Não reproduz transações nem o driver; esses ficam nos testes com Mongo real.
/// </summary>
public sealed class InMemoryOrcamentoRepository : IOrcamentoRepository
{
    public ConcurrentDictionary<Guid, Orcamento> Itens { get; } = new();
    private readonly ConcurrentDictionary<Guid, int> _versoes = new();

    public Task<Orcamento?> ObterPorIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Itens.GetValueOrDefault(id));

    public Task<Orcamento?> ObterPorOsAsync(Guid osId, CancellationToken ct = default)
        => Task.FromResult(Itens.Values.Where(o => o.OrdemServicoId == osId).OrderByDescending(o => o.CriadoEm).FirstOrDefault());

    public Task<IReadOnlyList<Orcamento>> ListarPorOsAsync(Guid osId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Orcamento>>(Itens.Values.Where(o => o.OrdemServicoId == osId).OrderByDescending(o => o.CriadoEm).ToList());

    public Task InserirAsync(Orcamento o, CancellationToken ct = default)
    {
        if (Itens.Values.Any(x => x.OrdemServicoId == o.OrdemServicoId)) throw new DuplicidadeException("orcamentos.OrdemServicoId");
        Itens[o.Id] = o;
        _versoes[o.Id] = o.Versao;
        return Task.CompletedTask;
    }

    public Task AtualizarAsync(Orcamento o, CancellationToken ct = default)
    {
        if (!_versoes.TryGetValue(o.Id, out var gravada) || gravada != o.Versao) throw new ConcorrenciaException("Orçamento", o.Id);
        o.AvancarVersao();
        Itens[o.Id] = o;
        _versoes[o.Id] = o.Versao;
        return Task.CompletedTask;
    }
}

public sealed class InMemoryPagamentoRepository : IPagamentoRepository
{
    public ConcurrentDictionary<Guid, Pagamento> Itens { get; } = new();
    private readonly ConcurrentDictionary<Guid, int> _versoes = new();

    public Task<Pagamento?> ObterPorIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Itens.GetValueOrDefault(id));

    public Task<Pagamento?> ObterPorOsAsync(Guid osId, CancellationToken ct = default)
        => Task.FromResult(Itens.Values.FirstOrDefault(p => p.OrdemServicoId == osId));

    public Task InserirAsync(Pagamento p, CancellationToken ct = default)
    {
        if (Itens.Values.Any(x => x.OrdemServicoId == p.OrdemServicoId)) throw new DuplicidadeException("pagamentos.OrdemServicoId");
        Itens[p.Id] = p;
        _versoes[p.Id] = p.Versao;
        return Task.CompletedTask;
    }

    public Task AtualizarAsync(Pagamento p, CancellationToken ct = default)
    {
        if (!_versoes.TryGetValue(p.Id, out var gravada) || gravada != p.Versao) throw new ConcorrenciaException("Pagamento", p.Id);
        p.AvancarVersao();
        Itens[p.Id] = p;
        _versoes[p.Id] = p.Versao;
        return Task.CompletedTask;
    }
}

public sealed class NoOpUnitOfWork : IUnitOfWork
{
    public Task<int> CommitAsync(CancellationToken ct = default) => Task.FromResult(0);
}