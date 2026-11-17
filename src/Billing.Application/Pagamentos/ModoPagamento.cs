namespace Billing.Application.Pagamentos;

/// <summary>Lê só o <c>Modo</c> da seção <c>MercadoPago</c> (Application não referencia Infrastructure).</summary>
public sealed class ModoPagamento
{
    public string Modo { get; set; } = "Fake";
    public bool Fake => !string.Equals(Modo, "Real", StringComparison.OrdinalIgnoreCase);
}
