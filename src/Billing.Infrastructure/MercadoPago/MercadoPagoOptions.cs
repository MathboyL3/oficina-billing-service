namespace Billing.Infrastructure.MercadoPago;

/// <summary>Seção <c>MercadoPago</c> do appsettings (ou env/Secret K8s).</summary>
public sealed class MercadoPagoOptions
{
    /// <summary>Real | Fake. Default Fake até existirem credenciais.</summary>
    public string Modo { get; set; } = "Fake";
    public string AccessToken { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public string PublicBaseUrl { get; set; } = "";
    public bool Sandbox { get; set; } = true;
    public string BaseUrl { get; set; } = "https://api.mercadopago.com";
    /// <summary>Só no Fake: Aprovar | Recusar | FalharEstorno.</summary>
    public string FakeComportamento { get; set; } = "Aprovar";
}
