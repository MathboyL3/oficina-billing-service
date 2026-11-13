using Billing.Domain.Common;
using Billing.Domain.Orcamentos;
using MassTransit;
using Microsoft.Extensions.Options;
using Oficina.Contracts;

namespace Billing.Application.Consumers;

/// <summary>Gera o orçamento da OS. Idempotente: orçamento Pendente existente é republicado.</summary>
public sealed class GerarOrcamentoConsumer : IConsumer<GerarOrcamento>
{
    private readonly IOrcamentoRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly BillingOptions _opt;

    public GerarOrcamentoConsumer(IOrcamentoRepository repo, IUnitOfWork uow, IOptions<BillingOptions> opt)
    {
        _repo = repo;
        _uow = uow;
        _opt = opt.Value;
    }

    public async Task Consume(ConsumeContext<GerarOrcamento> context)
    {
        var m = context.Message;
        var ct = context.CancellationToken;

        var existente = await _repo.ObterPorOsAsync(m.OrdemServicoId, ct);
        if (existente is { Status: StatusOrcamento.Pendente })
        {
            await context.Publish(new OrcamentoGerado(m.OrdemServicoId, DateTime.UtcNow, existente.Id, existente.ValorTotal, existente.ValidoAte), ct);
            await _uow.CommitAsync(ct);
            return;
        }

        try
        {
            var itens = (m.Itens ?? Array.Empty<ItemOrcamento>())
                .Select(i => i is null
                    ? throw new DomainException("Item de orçamento inválido.")
                    : new ItemOrcamentoDominio(i.Tipo, i.ItemId, i.Descricao, i.ValorUnitario, i.Quantidade))
                .ToList();
            var orc = Orcamento.Gerar(m.OrdemServicoId, m.ClienteId, itens, _opt.ValidadeOrcamento);
            await _repo.InserirAsync(orc, ct);
            await context.Publish(new OrcamentoGerado(m.OrdemServicoId, DateTime.UtcNow, orc.Id, orc.ValorTotal, orc.ValidoAte), ct);
        }
        catch (DomainException ex)
        {
            await context.Publish(new GeracaoOrcamentoFalhou(m.OrdemServicoId, DateTime.UtcNow, ex.Message), ct);
        }
        await _uow.CommitAsync(ct);
    }
}
