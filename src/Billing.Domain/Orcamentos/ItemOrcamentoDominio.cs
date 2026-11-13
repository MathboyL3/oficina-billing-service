namespace Billing.Domain.Orcamentos;

/// <summary>
/// Item do orçamento dentro do agregado. Mesma forma de <c>Oficina.Contracts.ItemOrcamento</c>,
/// mas o domínio não depende dos contratos de mensagem.
/// </summary>
public sealed record ItemOrcamentoDominio(string Tipo, Guid ItemId, string Descricao, decimal ValorUnitario, int Quantidade)
{
    public decimal Subtotal => ValorUnitario * Quantidade;
}
