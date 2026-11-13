using System.Diagnostics.CodeAnalysis;
using Billing.Domain.Common;

namespace Billing.Infrastructure.Persistence;

[ExcludeFromCodeCoverage]
public sealed class MongoUnitOfWork : IUnitOfWork
{
    private readonly MongoSessao _sessao;
    public MongoUnitOfWork(MongoSessao sessao) => _sessao = sessao;
    public Task<int> CommitAsync(CancellationToken ct = default) => _sessao.ConfirmarAsync(ct);
}
