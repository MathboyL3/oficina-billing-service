using Billing.Application.Common;
using Billing.Application.Pagamentos;
using Billing.Domain.Orcamentos;
using Billing.Domain.Pagamentos;
using MassTransit;
using Microsoft.Extensions.Logging;
using Oficina.Contracts;

namespace Billing.Application.UseCases;

/// <summary>Processa a notificação do MP consultando o pagamento na API (não confia no corpo).</summary>
public sealed class WebhookMercadoPagoUseCase
{
    private readonly IGatewayPagamento _gw;
    private readonly IPagamentoRepository _repo;
    private readonly IOrcamentoRepository _orcs;
    private readonly IPublishEndpoint _bus;
    private readonly ILogger<WebhookMercadoPagoUseCase> _log;

    public WebhookMercadoPagoUseCase(IGatewayPagamento gw, IPagamentoRepository repo, IOrcamentoRepository orcs, IPublishEndpoint bus,
        ILogger<WebhookMercadoPagoUseCase> log)
    {
        _gw = gw;
        _repo = repo;
        _orcs = orcs;
        _bus = bus;
        _log = log;
    }

    public async Task ProcessarAsync(string paymentId, CancellationToken ct)
    {
        var info = await _gw.ObterPagamentoAsync(paymentId, ct);
        await AplicarAsync(info, ct);
    }

    /// <summary>Também usado pelo modo Fake (sem webhook) logo após criar a cobrança.</summary>
    public async Task AplicarAsync(PagamentoInfo info, CancellationToken ct)
    {
        // external_reference da preferência = OrdemServicoId (há um pagamento por OS).
        if (!Guid.TryParse(info.ExternalReference, out var osId)) return;
        var pag = await _repo.ObterPorOsAsync(osId, ct);
        if (pag is null || pag.Status != StatusPagamento.Pendente) return; // desconhecido ou já processado

        switch (info.Status)
        {
            case StatusPagamentoGateway.Aprovado:
                var orc = await _orcs.ObterPorOsAsync(pag.OrdemServicoId, ct);
                var aposCancelamento = orc is { Status: StatusOrcamento.Cancelado or StatusOrcamento.Recusado };
                pag.Aprovar(info.PaymentId);
                // Registra a intenção antes de chamar o gateway: se o commit falhar depois do estorno real, o reprocessamento encontra EstornoPendente.
                if (aposCancelamento) pag.MarcarEstornoPendente();
                if (!await GravarAsync(pag, ct)) return;
                if (aposCancelamento)
                {
                    // OS já cancelada/recusada: a Saga não espera mais o pagamento, então devolve o dinheiro em vez de confirmar.
                    _log.LogWarning("{alerta}: pagamento {PagamentoId} da OS {OrdemServicoId} aprovado com orçamento {StatusOrcamento}; estornando",
                        "pagamento_apos_cancelamento", pag.Id, pag.OrdemServicoId, orc!.Status);
                    await EstornoPagamento.EstornarEPublicarAsync(_bus, _repo, _gw, pag, ct);
                    break;
                }
                await _bus.Publish(new PagamentoConfirmado(pag.OrdemServicoId, DateTime.UtcNow, pag.Id), ct);
                break;
            case StatusPagamentoGateway.Recusado:
                pag.Recusar(info.PaymentId);
                if (!await GravarAsync(pag, ct)) return;
                await _bus.Publish(new PagamentoRecusado(pag.OrdemServicoId, DateTime.UtcNow, pag.Id, info.StatusBruto), ct);
                break;
        }
    }

    /// <summary>Gravação otimista: se outro processamento (webhook x modo Fake x reentrega) ganhou, conta como já processado.</summary>
    private async Task<bool> GravarAsync(Pagamento pag, CancellationToken ct)
    {
        try
        {
            await _repo.AtualizarAsync(pag, ct);
            return true;
        }
        catch (ConcorrenciaException)
        {
            return false;
        }
    }
}