using System.Security.Cryptography;
using System.Text;

namespace Billing.Application.Pagamentos;

/// <summary>
/// Validação do header x-signature do Mercado Pago:
/// "ts=...,v1=HMAC_SHA256(secret, "id:{data.id};request-id:{x-request-id};ts:{ts};")".
/// </summary>
public static class AssinaturaWebhook
{
    public static bool Valida(string? xSignature, string? xRequestId, string? dataId, string secret,
        TimeSpan? tolerancia = null, DateTimeOffset? agora = null)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrWhiteSpace(xSignature) || string.IsNullOrWhiteSpace(dataId))
            return false;

        string? ts = null, v1 = null;
        foreach (var parte in xSignature.Split(','))
        {
            var kv = parte.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length != 2) continue;
            if (kv[0] == "ts") ts = kv[1];
            else if (kv[0] == "v1") v1 = kv[1];
        }
        if (ts is null || v1 is null || !long.TryParse(ts, out var tsNum)) return false;

        var instante = tsNum > 1_000_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(tsNum) : DateTimeOffset.FromUnixTimeSeconds(tsNum);
        if (((agora ?? DateTimeOffset.UtcNow) - instante).Duration() > (tolerancia ?? TimeSpan.FromMinutes(5)))
            return false;

        var id = dataId.All(char.IsLetterOrDigit) ? dataId.ToLowerInvariant() : dataId;
        var manifest = $"id:{id};request-id:{xRequestId};ts:{ts};";
        var esperado = Encoding.ASCII.GetBytes(Calcular(manifest, secret));
        var recebido = Encoding.ASCII.GetBytes(v1.ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(esperado, recebido);
    }

    public static string Calcular(string manifest, string secret)
        => Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(manifest)));
}