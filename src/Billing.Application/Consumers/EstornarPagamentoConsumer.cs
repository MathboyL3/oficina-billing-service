using Billing.Application.Pagamentos;
using Billing.Domain.Common;
using Billing.Domain.Pagamentos;
using MassTransit;
using Oficina.Contracts;

namespace Billing.Application.Consumers;

/// <summary>Compensação: estorna o pagamento. Idempotente; sempre responde PagamentoEstornado ou EstornoFalhou.</summary>
public sealed class EstornarPagamentoConsumer : IConsumer<EstornarPagamento>
{
    private readonly IPagamentoRepository _repo;
    private readonly IGatewayPagamento _gw;
    private readonly IUnitOfWork _uow;

    public EstornarPagamentoConsumer(IPagamentoRepository repo, IGatewayPagamento gw, IUnitOfWork uow)
    {
        _repo = repo;
        _gw = gw;
        _uow = uow;
    }

    public async Task Consume(ConsumeContext<EstornarPagamento> context)
    {
        var m = context.Message;
        var ct = context.CancellationToken;
        var pag = await _repo.ObterPorIdAsync(m.PagamentoId, ct);

        if (pag is null || pag.Status is StatusPagamento.Estornado or StatusPagamento.Recusado)
        {
            await Confirmar(context, m.OrdemServicoId, m.PagamentoId, ct);
            return;
        }

        if (pag.Status == StatusPagamento.Pendente)
        {
            var info = await _gw.BuscarPorReferenciaAsync(pag.Id.ToString(), ct);
            if (info is not { Status: StatusPagamentoGateway.Aprovado })
            {
                // Nunca foi pago: nada a estornar.
                await Confirmar(context, m.OrdemServicoId, pag.Id, ct);
                return;
            }
            pag.Aprovar(info.PaymentId);
        }

        await EstornoPagamento.EstornarEPublicarAsync(context, _repo, _gw, pag, ct);
        await _uow.CommitAsync(ct);
    }

    private async Task Confirmar(ConsumeContext<EstornarPagamento> context, Guid os, Guid pagamentoId, CancellationToken ct)
    {
        await context.Publish(new PagamentoEstornado(os, DateTime.UtcNow, pagamentoId), ct);
        await _uow.CommitAsync(ct);
    }
}
