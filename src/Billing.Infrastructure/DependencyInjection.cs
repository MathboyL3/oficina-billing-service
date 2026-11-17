using Billing.Application.Pagamentos;
using Billing.Domain.Common;
using Billing.Domain.Orcamentos;
using Billing.Domain.Pagamentos;
using Billing.Infrastructure.Auth;
using Billing.Infrastructure.MercadoPago;
using Billing.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Billing.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Em <paramref name="testing"/> a persistência Mongo não é registrada (os testes trazem repositórios em memória).</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration cfg, bool testing = false)
    {
        services.Configure<JwtOptions>(cfg.GetSection("Jwt"));
        services.AddGatewayPagamento(cfg);
        if (!testing) services.AddPersistenciaMongo(cfg);
        return services;
    }

    public static IServiceCollection AddPersistenciaMongo(this IServiceCollection services, IConfiguration cfg)
    {
        MongoMapeamentos.Registrar();
        services.Configure<MongoOptions>(cfg.GetSection("Mongo"));
        services.AddSingleton<IMongoClient>(_ => new MongoClient(cfg.GetConnectionString("Mongo")));
        services.AddSingleton<MongoContext>();
        services.AddScoped<MongoSessao>();
        services.AddScoped<IUnitOfWork, MongoUnitOfWork>();
        services.AddScoped<IOrcamentoRepository, OrcamentoRepository>();
        services.AddScoped<IPagamentoRepository, PagamentoRepository>();
        return services;
    }

    /// <summary>Escolhe o gateway por <c>MercadoPago:Modo</c> (Fake | Real); o default é Fake.</summary>
    public static IServiceCollection AddGatewayPagamento(this IServiceCollection services, IConfiguration cfg)
    {
        services.Configure<MercadoPagoOptions>(cfg.GetSection("MercadoPago"));
        var modo = cfg["MercadoPago:Modo"] ?? "Fake";
        if (string.Equals(modo, "Real", StringComparison.OrdinalIgnoreCase))
            services.AddHttpClient<IGatewayPagamento, MercadoPagoGateway>((sp, c) =>
                c.BaseAddress = new Uri(sp.GetRequiredService<IOptions<MercadoPagoOptions>>().Value.BaseUrl))
                .AddStandardResilienceHandler();
        else
            services.AddSingleton<IGatewayPagamento, FakeGatewayPagamento>();
        return services;
    }
}
