using System.Diagnostics.CodeAnalysis;
using MassTransit.DependencyInjection;
using MassTransit.MongoDbIntegration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Billing.Infrastructure.Persistence;

/// <summary>
/// Sessão/transação Mongo do escopo atual, compartilhada por repositórios e <see cref="MongoUnitOfWork"/>.
/// Dentro de um consumidor a transação já pertence ao filtro do outbox (que a confirma ao fim do consumo);
/// nos endpoints HTTP ela é aberta na primeira escrita e confirmada pelo <see cref="MongoUnitOfWork"/>,
/// junto com as mensagens do bus outbox. <c>MongoDbContext</c> só existe quando o outbox Mongo está ativo.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class MongoSessao
{
    private readonly MongoDbContext? _ctx;
    private readonly IScopedConsumeContextProvider? _consumo;

    public MongoSessao(IServiceProvider sp)
    {
        _ctx = sp.GetService<MongoDbContext>();
        _consumo = sp.GetService<IScopedConsumeContextProvider>();
    }

    /// <summary>Sessão corrente (leituras enxergam o que a transação já escreveu); nula sem outbox.</summary>
    public IClientSessionHandle? Atual => _ctx?.Session;

    public bool EmConsumidor => _consumo is { HasContext: true };

    /// <summary>Garante transação aberta antes de escrever (no-op quando já existe) e devolve a sessão.</summary>
    public async Task<IClientSessionHandle?> ParaEscritaAsync(CancellationToken ct)
    {
        if (_ctx is null) return null;
        if (_ctx.Session is not { IsInTransaction: true })
            await _ctx.BeginTransaction(ct);
        return _ctx.Session;
    }

    /// <summary>Confirma a transação do escopo HTTP. Em consumidor quem confirma é o outbox.</summary>
    public async Task<int> ConfirmarAsync(CancellationToken ct)
    {
        if (_ctx is null || EmConsumidor || _ctx.Session is not { IsInTransaction: true }) return 0;
        await _ctx.CommitTransaction(ct);
        return 1;
    }
}
