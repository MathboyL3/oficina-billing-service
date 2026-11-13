using System.Text.Json;
using Billing.Application.Pagamentos;
using Billing.Application.UseCases;
using Billing.Domain.Common;
using Billing.Infrastructure.MercadoPago;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Billing.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("webhooks/mercadopago")]
public class WebhooksController : ControllerBase
{
    private readonly WebhookMercadoPagoUseCase _uc;
    private readonly IUnitOfWork _uow;
    private readonly MercadoPagoOptions _opt;
    private readonly ILogger<WebhooksController> _log;

    public WebhooksController(WebhookMercadoPagoUseCase uc, IUnitOfWork uow, IOptions<MercadoPagoOptions> opt, ILogger<WebhooksController> log)
    {
        _uc = uc;
        _uow = uow;
        _opt = opt.Value;
        _log = log;
    }

    /// <summary>
    /// Notificação do Mercado Pago. Valida x-signature (HMAC) antes de tocar em qualquer coisa e consulta o
    /// pagamento na API. Assinatura inválida/ausente/ts malformado = 401; assinatura válida = 200 (o MP reenvia em caso de erro).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Receber(
        [FromQuery(Name = "data.id")] string? dataIdQuery,
        [FromQuery] string? type,
        [FromHeader(Name = "x-signature")] string? assinatura,
        [FromHeader(Name = "x-request-id")] string? requestId,
        CancellationToken ct)
    {
        var (dataIdCorpo, tipoCorpo) = await LerCorpoAsync(ct);
        var dataId = dataIdQuery ?? dataIdCorpo;
        var tipo = type ?? tipoCorpo;

        if (!AssinaturaWebhook.Valida(assinatura, requestId, dataId, _opt.WebhookSecret))
        {
            _log.LogWarning("Webhook MP com assinatura inválida (data.id={DataId})", dataId);
            return Unauthorized(new { status = 401, message = "Assinatura inválida." });
        }

        if (tipo == "payment" && dataId is not null)
        {
            try
            {
                await _uc.ProcessarAsync(dataId, ct);
                await _uow.CommitAsync(ct);
            }
            catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _log.LogInformation("Webhook MP ignorado: pagamento {DataId} desconhecido no gateway", dataId);
            }
        }
        return Ok();
    }

    /// <summary>Corpo é opcional e nunca derruba a requisição (a assinatura decide): lê data.id e type se for JSON válido.</summary>
    private async Task<(string? DataId, string? Tipo)> LerCorpoAsync(CancellationToken ct)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(Request.Body, cancellationToken: ct);
            var raiz = doc.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object) return (null, null);
            string? dataId = raiz.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object && d.TryGetProperty("id", out var id)
                ? id.ToString() : null;
            string? tipo = raiz.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            return (dataId, tipo);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}
