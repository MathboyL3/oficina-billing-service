namespace Billing.Application.Pagamentos;

public sealed record CriarCobrancaRequest(Guid PagamentoId, Guid OrdemServicoId, string Descricao, decimal Valor);
public sealed record CobrancaCriadaResult(string PreferenceId, string LinkPagamento);
public enum StatusPagamentoGateway { Pendente, Aprovado, Recusado, Estornado }
public sealed record PagamentoInfo(string PaymentId, StatusPagamentoGateway Status, string? ExternalReference, string StatusBruto);
public sealed record EstornoResult(bool Sucesso, string? Erro);

/// <summary>
/// Porta de saída para o provedor de pagamento (Mercado Pago). Implementada por
/// <c>MercadoPagoGateway</c> (REST) e <c>FakeGatewayPagamento</c> (testes/demo).
/// </summary>
public interface IGatewayPagamento
{
    Task<CobrancaCriadaResult> CriarCobrancaAsync(CriarCobrancaRequest r, CancellationToken ct);
    Task<PagamentoInfo> ObterPagamentoAsync(string paymentId, CancellationToken ct);
    Task<PagamentoInfo?> BuscarPorReferenciaAsync(string externalReference, CancellationToken ct);
    Task<EstornoResult> EstornarAsync(string paymentId, decimal? valor, CancellationToken ct);
}
