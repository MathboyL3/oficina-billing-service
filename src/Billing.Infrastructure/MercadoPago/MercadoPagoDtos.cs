namespace Billing.Infrastructure.MercadoPago;

// Corpo mínimo das respostas do Mercado Pago que o gateway consome.
internal sealed record PreferenciaResponse(string Id, string? InitPoint, string? SandboxInitPoint);
internal sealed record PaymentResponse(long Id, string? Status, string? ExternalReference);
internal sealed record SearchResponse(List<PaymentResponse>? Results);
