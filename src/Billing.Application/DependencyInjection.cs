using Billing.Application.Pagamentos;
using Billing.Application.UseCases;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Billing.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<BillingOptions>(config.GetSection("Billing"));
        services.Configure<ModoPagamento>(config.GetSection("MercadoPago"));
        services.AddScoped<WebhookMercadoPagoUseCase>();
        services.AddScoped<OrcamentoUseCases>();
        services.AddScoped<PagamentoUseCases>();
        return services;
    }
}
