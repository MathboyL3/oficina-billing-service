using Billing.Application.Common;
using Billing.Application.DTOs;
using Billing.Application.Mapping;
using Billing.Domain.Common;
using Billing.Domain.Orcamentos;
using Billing.Domain.Pagamentos;
using MassTransit;
using Oficina.Contracts;

namespace Billing.Application.UseCases;

public sealed class OrcamentoUseCases
{
    private readonly IOrcamentoRepository _orcs;
    private readonly IPagamentoRepository _pags;
    private readonly IPublishEndpoint _bus;
    private readonly IUnitOfWork _uow;

    public OrcamentoUseCases(IOrcamentoRepository orcs, IPagamentoRepository pags, IPublishEndpoint bus, IUnitOfWork uow)
    {
        _orcs = orcs;
        _pags = pags;
        _bus = bus;
        _uow = uow;
    }

    public async Task<OrcamentoResponse> ObterAsync(Guid id, CancellationToken ct = default)
        => (await Carregar(id, ct)).ToResponse();

    public async Task<IReadOnlyList<OrcamentoResponse>> ListarPorOsAsync(Guid osId, CancellationToken ct = default)
        => (await _orcs.ListarPorOsAsync(osId, ct)).Select(o => o.ToResponse()).ToList();

    public async Task<OrcamentoResponse> AprovarAsync(Guid id, Guid? clienteDoToken, CancellationToken ct = default)
    {
        var o = await Carregar(id, ct);
        GarantirDono(o, clienteDoToken);
        o.Aprovar(DateTimeOffset.UtcNow);
        await _orcs.AtualizarAsync(o, ct);
        await _bus.Publish(new OrcamentoAprovado(o.OrdemServicoId, DateTime.UtcNow, o.Id), ct);
        await _uow.CommitAsync(ct);
        return o.ToResponse();
    }

    public async Task<OrcamentoResponse> RecusarAsync(Guid id, RecusarOrcamentoRequest req, Guid? clienteDoToken, CancellationToken ct = default)
    {
        var o = await Carregar(id, ct);
        GarantirDono(o, clienteDoToken);
        var motivo = string.IsNullOrWhiteSpace(req?.Motivo) ? "Recusado pelo cliente." : req.Motivo;
        o.Recusar(motivo);
        await _orcs.AtualizarAsync(o, ct);
        await _bus.Publish(new OrcamentoRecusado(o.OrdemServicoId, DateTime.UtcNow, o.Id, motivo), ct);
        await _uow.CommitAsync(ct);
        return o.ToResponse();
    }

    public async Task<PagamentoResponse> ObterPagamentoAsync(Guid id, CancellationToken ct = default)
        => (await _pags.ObterPorIdAsync(id, ct) ?? throw new NotFoundException("Pagamento", id)).ToResponse();

    private async Task<Orcamento> Carregar(Guid id, CancellationToken ct)
        => await _orcs.ObterPorIdAsync(id, ct) ?? throw new NotFoundException("Orçamento", id);

    private static void GarantirDono(Orcamento o, Guid? cliente)
    {
        if (cliente is not null && cliente != o.ClienteId)
            throw new ForbiddenException("Orçamento não pertence ao cliente autenticado.");
    }
}
