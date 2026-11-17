using Billing.Application.Pagamentos;
using Billing.Domain.Pagamentos;
using MassTransit;
using Oficina.Contracts;

namespace Billing.Application.Common;

/// <summary>Estorno compartilhado pelos consumidores de compensação.</summary>
internal static class EstornoPagamento
{
    /// <summary>
    /// Estorna o pagamento Aprovado/EstornoPendente e publica PagamentoEstornado ou EstornoFalhou (sem commit).
    /// Consulta o MP antes: já estornado (refunded) lá só marca Estornado, sem nova chamada de estorno.
    /// Se a consulta prévia falhar, estorna mesmo assim (não bloqueia o estorno). Se o estorno falhar, reconsulta o MP.
    /// </summary>
    public static async Task EstornarEPublicarAsync(IPublishEndpoint bus, IPagamentoRepository repo, IGatewayPagamento gw,
        Pagamento pag, CancellationToken ct)
    {
        var paymentId = pag.PaymentIdMp;
        var temId = !string.IsNullOrWhiteSpace(paymentId);
        var jaEstornado = temId && await JaEstornadoNoMpAsync(gw, paymentId!, ct);
        var r = jaEstornado ? new EstornoResult(true, null)
            : !temId ? new EstornoResult(false, "Pagamento sem identificador no gateway (PaymentIdMp ausente).")
            : await gw.EstornarAsync(paymentId!, null, ct);
        var ok = r.Sucesso || (temId && await JaEstornadoNoMpAsync(gw, paymentId!, ct));
        if (ok)
        {
            pag.MarcarEstornado();
            await repo.AtualizarAsync(pag, ct);
            await bus.Publish(new PagamentoEstornado(pag.OrdemServicoId, DateTime.UtcNow, pag.Id), ct);
        }
        else
        {
            pag.MarcarEstornoPendente();
            await repo.AtualizarAsync(pag, ct);
            await bus.Publish(new EstornoFalhou(pag.OrdemServicoId, DateTime.UtcNow, pag.Id, r.Erro ?? "Falha ao estornar no gateway."), ct);
        }
    }

    private static async Task<bool> JaEstornadoNoMpAsync(IGatewayPagamento gw, string paymentId, CancellationToken ct)
    {
        try
        {
            var info = await gw.ObterPagamentoAsync(paymentId, ct);
            return info is { Status: StatusPagamentoGateway.Estornado };
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return false; // sem confirmação do MP: mantém como falha
        }
    }
}
