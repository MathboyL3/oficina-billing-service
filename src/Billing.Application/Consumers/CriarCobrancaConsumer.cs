using Billing.Application.Common;
using Billing.Application.Pagamentos;
using Billing.Application.UseCases;
using Billing.Domain.Common;
using Billing.Domain.Pagamentos;
using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oficina.Contracts;

namespace Billing.Application.Consumers;

/// <summary>
/// Cria a cobrança no gateway. Idempotente: pagamento da OS já com preferência
/// republica CobrancaCriada com o link existente, sem nova chamada ao gateway.
/// </summary>
public sealed class CriarCobrancaConsumer : IConsumer<CriarCobranca>
{
    private readonly IPagamentoRepository _repo;
    private readonly IGatewayPagamento _gw;
    private readonly WebhookMercadoPagoUseCase _webhook;
    private readonly IUnitOfWork _uow;
    private readonly ModoPagamento _modo;
    private readonly ILogger<CriarCobrancaConsumer> _log;

    public CriarCobrancaConsumer(IPagamentoRepository repo, IGatewayPagamento gw, WebhookMercadoPagoUseCase webhook,
        IUnitOfWork uow, IOptions<ModoPagamento> modo, ILogger<CriarCobrancaConsumer> log)
    {
        _log = log;
        _repo = repo;
        _gw = gw;
        _webhook = webhook;
        _uow = uow;
        _modo = modo.Value;
    }

    public async Task Consume(ConsumeContext<CriarCobranca> context)
    {
        var m = context.Message;
        var ct = context.CancellationToken;

        var pag = await _repo.ObterPorOsAsync(m.OrdemServicoId, ct);
        if (pag is null)
        {
            pag = Pagamento.Criar(m.OrdemServicoId, m.OrcamentoId, m.Valor);
            // Insere antes de cobrar: o índice único por OS impede que entregas concorrentes criem duas preferências no gateway.
            // DuplicidadeException propaga de propósito: um E11000 aborta a transação Mongo, então o retry do MassTransit
            // reprocessa a mensagem e ela cai no caminho idempotente (pagamento já existente).
            await _repo.InserirAsync(pag, ct);
            await EmitirCobranca(pag, ct);
            if (!await GravarAsync(pag, ct)) return;
        }
        else if (pag.PreferenceId is null && pag.Status == StatusPagamento.Pendente)
        {
            await EmitirCobranca(pag, ct);
            if (!await GravarAsync(pag, ct)) return;
        }

        await context.Publish(new CobrancaCriada(m.OrdemServicoId, DateTime.UtcNow, pag.Id, pag.LinkPagamento ?? ""), ct);

        // Modo Fake: não há webhook — confirma consultando o gateway em memória.
        if (_modo.Fake && pag.Status == StatusPagamento.Pendente)
        {
            var info = await _gw.BuscarPorReferenciaAsync(pag.OrdemServicoId.ToString(), ct);
            if (info is not null) await _webhook.AplicarAsync(info, ct);
        }

        await _uow.CommitAsync(ct);
    }

    /// <summary>Gravação otimista: perdeu a corrida para outro processamento (webhook, reentrega) = já processado, sem republicar.</summary>
    private async Task<bool> GravarAsync(Pagamento pag, CancellationToken ct)
    {
        try
        {
            await _repo.AtualizarAsync(pag, ct);
            return true;
        }
        catch (ConcorrenciaException)
        {
            _log.LogInformation("CriarCobranca ignorado: pagamento {PagamentoId} da OS {OrdemServicoId} foi alterado por outro processamento", pag.Id, pag.OrdemServicoId);
            return false;
        }
    }

    private async Task EmitirCobranca(Pagamento pag, CancellationToken ct)
    {
        var cobranca = await _gw.CriarCobrancaAsync(
            new CriarCobrancaRequest(pag.Id, pag.OrdemServicoId, $"Ordem de Serviço {pag.OrdemServicoId}", pag.Valor), ct);
        pag.RegistrarPreferencia(cobranca.PreferenceId, cobranca.LinkPagamento);
    }
}
