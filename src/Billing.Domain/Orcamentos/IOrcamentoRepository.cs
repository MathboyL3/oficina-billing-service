namespace Billing.Domain.Orcamentos;

public interface IOrcamentoRepository
{
    Task<Orcamento?> ObterPorIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Orçamento mais recente da OS (uma OS tem no máximo um orçamento corrente).</summary>
    Task<Orcamento?> ObterPorOsAsync(Guid osId, CancellationToken ct = default);

    Task<IReadOnlyList<Orcamento>> ListarPorOsAsync(Guid osId, CancellationToken ct = default);
    Task InserirAsync(Orcamento orcamento, CancellationToken ct = default);
    Task AtualizarAsync(Orcamento orcamento, CancellationToken ct = default);
}
