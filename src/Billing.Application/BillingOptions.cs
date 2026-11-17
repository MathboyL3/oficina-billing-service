namespace Billing.Application;

/// <summary>Seção <c>Billing</c> do appsettings.</summary>
public sealed class BillingOptions
{
    public TimeSpan ValidadeOrcamento { get; set; } = TimeSpan.FromHours(72);
}
