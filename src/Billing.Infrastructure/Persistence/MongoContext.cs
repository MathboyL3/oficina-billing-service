using System.Diagnostics.CodeAnalysis;
using Billing.Domain.Orcamentos;
using Billing.Domain.Pagamentos;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Billing.Infrastructure.Persistence;

/// <summary>
/// Acesso às coleções do billing. Cria os índices no construtor (<c>CreateOne</c> é idempotente):
/// um orçamento e um pagamento por OS — a unicidade barra a reentrega concorrente que passou da checagem de existência.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class MongoContext
{
    public IMongoDatabase Database { get; }
    public IMongoCollection<Orcamento> Orcamentos { get; }
    public IMongoCollection<Pagamento> Pagamentos { get; }

    public MongoContext(IMongoClient client, IOptions<MongoOptions> opt)
    {
        Database = client.GetDatabase(opt.Value.Database);
        Orcamentos = Database.GetCollection<Orcamento>("orcamentos");
        Pagamentos = Database.GetCollection<Pagamento>("pagamentos");

        Orcamentos.Indexes.CreateOne(new CreateIndexModel<Orcamento>(
            Builders<Orcamento>.IndexKeys.Ascending(o => o.OrdemServicoId),
            new CreateIndexOptions { Unique = true, Name = "ux_orcamentos_os" }));
        Pagamentos.Indexes.CreateOne(new CreateIndexModel<Pagamento>(
            Builders<Pagamento>.IndexKeys.Ascending(p => p.OrdemServicoId),
            new CreateIndexOptions { Unique = true, Name = "ux_pagamentos_os" }));
    }
}
