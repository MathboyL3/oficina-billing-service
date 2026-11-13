using Billing.Application.Pagamentos;
using Billing.Domain.Pagamentos;
using MassTransit;
using Oficina.Contracts;

namespace Billing.Application.UseCases;

/// <summary>Processa a notificação do MP consultando o pagamento na API (não confia no corpo).</summary>
public sealed class WebhookMercadoPagoUseCase
{
    private readonly IGatewayPagamento _gw;
    private readonly IPagamentoRepository _repo;
    private readonly IPublishEndpoint _bus;

    public WebhookMercadoPagoUseCase(IGatewayPagamento gw, IPagamentoRepository repo, IPublishEndpoint bus)
    {
        _gw = gw;
        _repo = repo;
        _bus = bus;
    }

    public async Task ProcessarAsync(string paymentId, CancellationToken ct)
    {
        var info = await _gw.ObterPagamentoAsync(paymentId, ct);
        await AplicarAsync(info, ct);
    }

    /// <summary>Também usado pelo modo Fake (sem webhook) logo após criar a cobrança.</summary>
    public async Task AplicarAsync(PagamentoInfo info, CancellationToken ct)
    {
        if (!Guid.TryParse(info.ExternalReference, out var pagamentoId)) return;
        var pag = await _repo.ObterPorIdAsync(pagamentoId, ct);
        if (pag is null || pag.Status != StatusPagamento.Pendente) return; // desconhecido ou já processado

        switch (info.Status)
        {
            case StatusPagamentoGateway.Aprovado:
                pag.Aprovar(info.PaymentId);
                await _repo.AtualizarAsync(pag, ct);
                await _bus.Publish(new PagamentoConfirmado(pag.OrdemServicoId, DateTime.UtcNow, pag.Id), ct);
                break;
            case StatusPagamentoGateway.Recusado:
                pag.Recusar(info.PaymentId);
                await _repo.AtualizarAsync(pag, ct);
                await _bus.Publish(new PagamentoRecusado(pag.OrdemServicoId, DateTime.UtcNow, pag.Id, info.StatusBruto), ct);
                break;
        }
    }
}