using System.Diagnostics.CodeAnalysis;
using Billing.Application.Consumers;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Billing.Infrastructure.Messaging;

[ExcludeFromCodeCoverage]
public static class MessagingExtensions
{
    /// <summary>
    /// RabbitMQ + outbox transacional Mongo (publicações só saem após o commit da transação, que também
    /// grava o inbox de duplicidade por MessageId) + retry exponencial. Em "Testing" usa o transporte em memória.
    /// Exige MongoDB em replica set (transações).
    /// </summary>
    public static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration cfg, bool testing)
    {
        services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();
            x.AddConsumers(typeof(GerarOrcamentoConsumer).Assembly);

            if (!testing)
            {
                x.AddMongoDbOutbox(o =>
                {
                    o.QueryDelay = TimeSpan.FromSeconds(1);
                    o.ClientFactory(_ => new MongoClient(cfg.GetConnectionString("Mongo")));
                    o.DatabaseFactory(p => p.GetRequiredService<IMongoClient>().GetDatabase(cfg["Mongo:Database"] ?? "oficina_billing"));
                    o.UseBusOutbox();
                });
            }

            x.AddConfigureEndpointsCallback((ctx, _, ep) =>
            {
                ep.UseMessageRetry(r => r.Exponential(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2)));
                if (!testing) ep.UseMongoDbOutbox(ctx);
            });

            if (testing)
            {
                x.UsingInMemory((ctx, c) => c.ConfigureEndpoints(ctx));
                return;
            }

            x.UsingRabbitMq((ctx, c) =>
            {
                var rabbit = RabbitMqOptions.FromConfiguration(cfg);
                c.Host(rabbit.Host, (ushort)rabbit.Port, rabbit.VirtualHost, h =>
                {
                    h.Username(rabbit.Username);
                    h.Password(rabbit.Password);
                });
                c.ConfigureEndpoints(ctx);
            });
        });
        return services;
    }
}
