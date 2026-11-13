using Billing.Application.DTOs;
using Billing.Domain.Orcamentos;
using Billing.Domain.Pagamentos;

namespace Billing.Application.Mapping;

public static class Mapper
{
    public static OrcamentoResponse ToResponse(this Orcamento o) =>
        new(o.Id, o.OrdemServicoId, o.ClienteId, o.Itens.ToList(), o.ValorTotal, o.Status.ToString(), o.ValidoAte, o.MotivoRecusa);

    public static PagamentoResponse ToResponse(this Pagamento p) =>
        new(p.Id, p.OrdemServicoId, p.Valor, p.Status.ToString(), p.LinkPagamento);
}
