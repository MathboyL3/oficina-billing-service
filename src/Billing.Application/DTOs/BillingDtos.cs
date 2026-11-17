using Billing.Domain.Orcamentos;

namespace Billing.Application.DTOs;

public sealed record OrcamentoResponse(Guid Id, Guid OrdemServicoId, Guid ClienteId, IReadOnlyList<ItemOrcamentoDominio> Itens,
    decimal ValorTotal, string Status, DateTime ValidoAte, string? MotivoRecusa);

public sealed record PagamentoResponse(Guid Id, Guid OrdemServicoId, decimal Valor, string Status, string? LinkPagamento);

public sealed record RecusarOrcamentoRequest(string Motivo);
