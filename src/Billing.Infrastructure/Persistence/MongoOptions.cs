namespace Billing.Infrastructure.Persistence;

/// <summary>Seção <c>Mongo</c> do appsettings (a connection string fica em <c>ConnectionStrings:Mongo</c>).</summary>
public sealed class MongoOptions
{
    public string Database { get; set; } = "oficina_billing";
}
