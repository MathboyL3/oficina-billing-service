using Billing.Application.Common;
using Billing.Application.DTOs;
using Billing.Application.Mapping;
using Billing.Application.Pagamentos;
using Billing.Domain.Common;
using Billing.Domain.Pagamentos;
using MassTransit;

namespace Billing.Application.UseCases;

public sealed class PagamentoUseCases
{
    private readonly IPagamentoRepository _pags;
    private readonly IGatewayPagamento _gw;
    private readonly IPublishEndpoint _bus;
    private readonly IUnitOfWork _uow;

    public PagamentoUseCases(IPagamentoRepository pags, IGatewayPagamento gw, IPublishEndpoint bus, IUnitOfWork uow)
    {
        _pags = pags;
        _gw = gw;
        _bus = bus;
        _uow = uow;
    }

    /// <summary>Reexecuta o estorno de um pagamento em EstornoPendente (reprocessamento manual, sem Saga).</summary>
    public async Task<PagamentoResponse> ReprocessarEstornoAsync(Guid id, CancellationToken ct = default)
    {
        var p = await _pags.ObterPorIdAsync(id, ct) ?? throw new NotFoundException("Pagamento", id);
        if (p.Status != StatusPagamento.EstornoPendente)
            throw new ConflictException($"Pagamento no status {p.Status}; só EstornoPendente pode ser reprocessado.");
        await EstornoPagamento.EstornarEPublicarAsync(_bus, _pags, _gw, p, ct);
        await _uow.CommitAsync(ct);
        return p.ToResponse();
    }
}