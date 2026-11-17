namespace Billing.Domain.Pagamentos;

public interface IPagamentoRepository
{
    Task<Pagamento?> ObterPorIdAsync(Guid id, CancellationToken ct = default);
    Task<Pagamento?> ObterPorOsAsync(Guid osId, CancellationToken ct = default);
    Task InserirAsync(Pagamento pagamento, CancellationToken ct = default);
    Task AtualizarAsync(Pagamento pagamento, CancellationToken ct = default);
}
