using Billing.Application.Pagamentos;
using Billing.Domain.Common;
using Billing.Domain.Orcamentos;
using Billing.Domain.Pagamentos;
using MassTransit;
using Oficina.Contracts;

namespace Billing.Application.Consumers;

/// <summary>
/// Compensação. Antes de cancelar, confere no MP se o pagamento da OS já foi aprovado
/// (corrida pagamento × cancelamento): se sim, estorna primeiro. Sempre confirma com OrcamentoCancelado.
/// </summary>
public sealed class CancelarOrcamentoConsumer : IConsumer<CancelarOrcamento>
{
    private readonly IOrcamentoRepository _orcs;
    private readonly IPagamentoRepository _pags;
    private readonly IGatewayPagamento _gw;
    private readonly IUnitOfWork _uow;

    public CancelarOrcamentoConsumer(IOrcamentoRepository orcs, IPagamentoRepository pags, IGatewayPagamento gw, IUnitOfWork uow)
    {
        _orcs = orcs;
        _pags = pags;
        _gw = gw;
        _uow = uow;
    }

    public async Task Consume(ConsumeContext<CancelarOrcamento> context)
    {
        var m = context.Message;
        var ct = context.CancellationToken;

        var pag = await _pags.ObterPorOsAsync(m.OrdemServicoId, ct);
        if (pag is { Status: StatusPagamento.Pendente })
        {
            var info = await _gw.BuscarPorReferenciaAsync(pag.Id.ToString(), ct);
            if (info is { Status: StatusPagamentoGateway.Aprovado })
            {
                pag.Aprovar(info.PaymentId);
                await EstornoPagamento.EstornarEPublicarAsync(context, _pags, _gw, pag, ct);
            }
        }
        else if (pag is { Status: StatusPagamento.Aprovado or StatusPagamento.EstornoPendente })
        {
            await EstornoPagamento.EstornarEPublicarAsync(context, _pags, _gw, pag, ct);
        }

        var orc = await _orcs.ObterPorOsAsync(m.OrdemServicoId, ct);
        if (orc is not null)
        {
            orc.Cancelar();
            await _orcs.AtualizarAsync(orc, ct);
        }
        await context.Publish(new OrcamentoCancelado(m.OrdemServicoId, DateTime.UtcNow, orc?.Id), ct);
        await _uow.CommitAsync(ct);
    }
}
