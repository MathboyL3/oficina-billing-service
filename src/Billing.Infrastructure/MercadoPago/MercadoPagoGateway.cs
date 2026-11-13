using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Billing.Application.Pagamentos;
using Microsoft.Extensions.Options;

namespace Billing.Infrastructure.MercadoPago;

/// <summary>Integração REST com o Mercado Pago (Checkout Pro + Payments API), sem SDK.</summary>
public sealed class MercadoPagoGateway : IGatewayPagamento
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly MercadoPagoOptions _opt;

    public MercadoPagoGateway(HttpClient http, IOptions<MercadoPagoOptions> opt)
    {
        _http = http;
        _opt = opt.Value;
    }

    /// <summary>Preferência do Checkout Pro; a <c>external_reference</c> é o id do nosso Pagamento.</summary>
    public async Task<CobrancaCriadaResult> CriarCobrancaAsync(CriarCobrancaRequest r, CancellationToken ct)
    {
        var body = new
        {
            items = new[] { new { id = r.OrdemServicoId.ToString(), title = r.Descricao, quantity = 1, currency_id = "BRL", unit_price = r.Valor } },
            external_reference = r.PagamentoId.ToString(),
            notification_url = $"{_opt.PublicBaseUrl.TrimEnd('/')}/webhooks/mercadopago"
        };
        using var req = Request(HttpMethod.Post, "/checkout/preferences");
        req.Headers.Add("X-Idempotency-Key", r.PagamentoId.ToString());
        req.Content = JsonContent.Create(body, options: Json);
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var pref = await resp.Content.ReadFromJsonAsync<PreferenciaResponse>(Json, ct)
            ?? throw new HttpRequestException("Resposta vazia do Mercado Pago.");
        var link = _opt.Sandbox ? pref.SandboxInitPoint ?? pref.InitPoint : pref.InitPoint;
        if (string.IsNullOrWhiteSpace(link))
            throw new HttpRequestException("Mercado Pago não retornou init_point/sandbox_init_point.");
        return new CobrancaCriadaResult(pref.Id, link);
    }

    public async Task<PagamentoInfo> ObterPagamentoAsync(string paymentId, CancellationToken ct)
    {
        using var req = Request(HttpMethod.Get, $"/v1/payments/{Uri.EscapeDataString(paymentId)}");
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var p = await resp.Content.ReadFromJsonAsync<PaymentResponse>(Json, ct)
            ?? throw new HttpRequestException("Resposta vazia do Mercado Pago.");
        return Mapear(p);
    }

    /// <summary>
    /// Busca o pagamento pela <c>external_reference</c>; desempata a corrida entre pagamento e
    /// cancelamento (o mais recente é o que vale).
    /// </summary>
    public async Task<PagamentoInfo?> BuscarPorReferenciaAsync(string externalReference, CancellationToken ct)
    {
        using var req = Request(HttpMethod.Get,
            $"/v1/payments/search?external_reference={Uri.EscapeDataString(externalReference)}&sort=date_created&criteria=desc");
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var s = await resp.Content.ReadFromJsonAsync<SearchResponse>(Json, ct);
        var first = s?.Results?.FirstOrDefault();
        return first is null ? null : Mapear(first);
    }

    /// <summary>Estorno (total quando <paramref name="valor"/> é nulo); erro volta em <see cref="EstornoResult"/>.</summary>
    public async Task<EstornoResult> EstornarAsync(string paymentId, decimal? valor, CancellationToken ct)
    {
        using var req = Request(HttpMethod.Post, $"/v1/payments/{Uri.EscapeDataString(paymentId)}/refunds");
        req.Headers.Add("X-Idempotency-Key", $"refund-{paymentId}");
        req.Content = JsonContent.Create(valor is null ? new { } : (object)new { amount = valor }, options: Json);
        try
        {
            using var resp = await _http.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode) return new EstornoResult(true, null);
            var erro = await resp.Content.ReadAsStringAsync(ct);
            return new EstornoResult(false, $"{(int)resp.StatusCode}: {erro}");
        }
        catch (Exception e) when (e is HttpRequestException || (e is TaskCanceledException && !ct.IsCancellationRequested))
        {
            return new EstornoResult(false, $"Falha de transporte: {e.Message}");
        }
    }

    private HttpRequestMessage Request(HttpMethod m, string path)
    {
        var req = new HttpRequestMessage(m, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opt.AccessToken);
        return req;
    }

    private static PagamentoInfo Mapear(PaymentResponse p)
    {
        var status = p.Status switch
        {
            "approved" => StatusPagamentoGateway.Aprovado,
            "rejected" or "cancelled" => StatusPagamentoGateway.Recusado,
            "refunded" or "charged_back" => StatusPagamentoGateway.Estornado,
            _ => StatusPagamentoGateway.Pendente
        };
        return new PagamentoInfo(p.Id.ToString(CultureInfo.InvariantCulture), status, p.ExternalReference, p.Status ?? "");
    }
}
