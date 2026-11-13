using Billing.Domain.Common;
using Billing.Domain.Orcamentos;
using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oficina.Contracts;

namespace Billing.Application.Consumers;

/// <summary>Gera o orçamento da OS. Idempotente: orçamento Pendente existente é republicado; em outro status só registra log.</summary>
public sealed class GerarOrcamentoConsumer : IConsumer<GerarOrcamento>
{
    private readonly IOrcamentoRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly BillingOptions _opt;
    private readonly ILogger<GerarOrcamentoConsumer> _log;

    public GerarOrcamentoConsumer(IOrcamentoRepository repo, IUnitOfWork uow, IOptions<BillingOptions> opt, ILogger<GerarOrcamentoConsumer> log)
    {
        _log = log;
        _repo = repo;
        _uow = uow;
        _opt = opt.Value;
    }

    public async Task Consume(ConsumeContext<GerarOrcamento> context)
    {
        var m = context.Message;
        var ct = context.CancellationToken;

        var existente = await _repo.ObterPorOsAsync(m.OrdemServicoId, ct);
        if (existente is not null)
        {
            if (existente.Status == StatusOrcamento.Pendente)
            {
                await context.Publish(new OrcamentoGerado(m.OrdemServicoId, DateTime.UtcNow, existente.Id, existente.ValorTotal, existente.ValidoAte), ct);
                await _uow.CommitAsync(ct);
            }
            else
            {
                _log.LogInformation("GerarOrcamento ignorado: OS {OrdemServicoId} já tem orçamento {OrcamentoId} em {Status}", m.OrdemServicoId, existente.Id, existente.Status);
            }
            return;
        }

        try
        {
            var itens = (m.Itens ?? Array.Empty<ItemOrcamento>())
                .Select(i => i is null
                    ? throw new DomainException("Item de orçamento inválido.")
                    : string.IsNullOrWhiteSpace(i.Descricao)
                    ? throw new DomainException("Item de orçamento sem descrição.")
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
