using System.Diagnostics.CodeAnalysis;
using Billing.Application.Common;
using Billing.Domain.Pagamentos;
using MongoDB.Driver;

namespace Billing.Infrastructure.Persistence;

/// <summary>Mesma estratégia de sessão e de controle otimista (<c>Versao</c>) do <see cref="OrcamentoRepository"/>.</summary>
[ExcludeFromCodeCoverage]
public sealed class PagamentoRepository : IPagamentoRepository
{
    private readonly MongoContext _db;
    private readonly MongoSessao _sessao;

    public PagamentoRepository(MongoContext db, MongoSessao sessao)
    {
        _db = db;
        _sessao = sessao;
    }

    private IFindFluent<Pagamento, Pagamento> Buscar(System.Linq.Expressions.Expression<Func<Pagamento, bool>> filtro)
        => _sessao.Atual is { } s ? _db.Pagamentos.Find(s, filtro) : _db.Pagamentos.Find(filtro);

    public Task<Pagamento?> ObterPorIdAsync(Guid id, CancellationToken ct = default)
        => Buscar(p => p.Id == id).FirstOrDefaultAsync(ct)!;

    public Task<Pagamento?> ObterPorOsAsync(Guid osId, CancellationToken ct = default)
        => Buscar(p => p.OrdemServicoId == osId).FirstOrDefaultAsync(ct)!;

    public async Task InserirAsync(Pagamento p, CancellationToken ct = default)
    {
        var s = await _sessao.ParaEscritaAsync(ct);
        try
        {
            if (s is not null) await _db.Pagamentos.InsertOneAsync(s, p, cancellationToken: ct);
            else await _db.Pagamentos.InsertOneAsync(p, cancellationToken: ct);
        }
        catch (Exception e) when (e.EhChaveDuplicada())
        {
            throw new DuplicidadeException($"Já existe pagamento para a OS {p.OrdemServicoId}.");
        }
    }

    public async Task AtualizarAsync(Pagamento p, CancellationToken ct = default)
    {
        var lida = p.Versao;
        p.AvancarVersao();
        var s = await _sessao.ParaEscritaAsync(ct);
        var opcoes = new ReplaceOptions { IsUpsert = false };
        var r = s is not null
            ? await _db.Pagamentos.ReplaceOneAsync(s, x => x.Id == p.Id && x.Versao == lida, p, opcoes, ct)
            : await _db.Pagamentos.ReplaceOneAsync(x => x.Id == p.Id && x.Versao == lida, p, opcoes, ct);
        if (r.MatchedCount == 0) throw new ConcorrenciaException("Pagamento", p.Id);
    }
}
