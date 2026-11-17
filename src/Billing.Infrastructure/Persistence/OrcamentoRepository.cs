using Billing.Application.Common;
using Billing.Domain.Orcamentos;
using MongoDB.Driver;

namespace Billing.Infrastructure.Persistence;

/// <summary>
/// Leituras e escritas usam a sessão do escopo (outbox do MassTransit) quando existe, para que a escrita
/// e a mensagem publicada caiam na mesma transação e as leituras enxerguem o que ela já gravou.
/// <see cref="AtualizarAsync"/> é otimista: só grava se a <c>Versao</c> lida ainda for a do documento.
/// </summary>
public sealed class OrcamentoRepository : IOrcamentoRepository
{
    private readonly MongoContext _db;
    private readonly MongoSessao _sessao;

    public OrcamentoRepository(MongoContext db, MongoSessao sessao)
    {
        _db = db;
        _sessao = sessao;
    }

    private IFindFluent<Orcamento, Orcamento> Buscar(System.Linq.Expressions.Expression<Func<Orcamento, bool>> filtro)
        => _sessao.Atual is { } s ? _db.Orcamentos.Find(s, filtro) : _db.Orcamentos.Find(filtro);

    public Task<Orcamento?> ObterPorIdAsync(Guid id, CancellationToken ct = default)
        => Buscar(o => o.Id == id).FirstOrDefaultAsync(ct)!;

    public Task<Orcamento?> ObterPorOsAsync(Guid osId, CancellationToken ct = default)
        => Buscar(o => o.OrdemServicoId == osId).SortByDescending(o => o.CriadoEm).FirstOrDefaultAsync(ct)!;

    public async Task<IReadOnlyList<Orcamento>> ListarPorOsAsync(Guid osId, CancellationToken ct = default)
        => await Buscar(o => o.OrdemServicoId == osId).SortByDescending(o => o.CriadoEm).ToListAsync(ct);

    public async Task InserirAsync(Orcamento o, CancellationToken ct = default)
    {
        var s = await _sessao.ParaEscritaAsync(ct);
        try
        {
            if (s is not null) await _db.Orcamentos.InsertOneAsync(s, o, cancellationToken: ct);
            else await _db.Orcamentos.InsertOneAsync(o, cancellationToken: ct);
        }
        catch (Exception e) when (e.EhChaveDuplicada())
        {
            throw new DuplicidadeException($"Já existe orçamento para a OS {o.OrdemServicoId}.");
        }
    }

    public async Task AtualizarAsync(Orcamento o, CancellationToken ct = default)
    {
        var lida = o.Versao;
        var s = await _sessao.ParaEscritaAsync(ct);
        var opcoes = new ReplaceOptions { IsUpsert = false };
        // A versão avança antes do replace (o documento gravado já leva a nova) e é desfeita se a gravação não acontecer.
        o.AvancarVersao();
        try
        {
            var r = s is not null
                ? await _db.Orcamentos.ReplaceOneAsync(s, x => x.Id == o.Id && x.Versao == lida, o, opcoes, ct)
                : await _db.Orcamentos.ReplaceOneAsync(x => x.Id == o.Id && x.Versao == lida, o, opcoes, ct);
            if (r.MatchedCount == 0) throw new ConcorrenciaException("Orçamento", o.Id);
        }
        catch
        {
            o.ReverterVersao();
            throw;
        }
    }
}
