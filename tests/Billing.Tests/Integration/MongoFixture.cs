using Billing.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Testcontainers.MongoDb;

namespace Billing.Tests.Integration;

/// <summary>MongoDB 7 real em replica set (transações) via Testcontainers, compartilhado pela coleção "mongo".</summary>
public sealed class MongoFixture : IAsyncLifetime
{
    private readonly MongoDbContainer _container = new MongoDbBuilder("mongo:7").WithReplicaSet().Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>Banco novo por teste (índices únicos e dados isolados).</summary>
    public MongoContext NovoContexto(out string banco)
    {
        MongoMapeamentos.Registrar();
        banco = "billing_" + Guid.NewGuid().ToString("N")[..10];
        return new MongoContext(new MongoClient(ConnectionString), Options.Create(new MongoOptions { Database = banco }));
    }
}

[CollectionDefinition("mongo")]
public sealed class MongoCollection : ICollectionFixture<MongoFixture> { }