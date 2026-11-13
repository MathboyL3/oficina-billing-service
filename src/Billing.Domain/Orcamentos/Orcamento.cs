using Billing.Domain.Common;

namespace Billing.Domain.Orcamentos;

public sealed class Orcamento
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid OrdemServicoId { get; private set; }
    public Guid ClienteId { get; private set; }
    public List<ItemOrcamentoDominio> Itens { get; private set; } = new();
    public decimal ValorTotal { get; private set; }
    public StatusOrcamento Status { get; private set; }
    public DateTime CriadoEm { get; private set; }
    public DateTime ValidoAte { get; private set; }
    public string? MotivoRecusa { get; private set; }

    private Orcamento() { }

    public static Orcamento Gerar(Guid ordemServicoId, Guid clienteId, IEnumerable<ItemOrcamentoDominio> itens, TimeSpan validade)
    {
        var lista = itens?.ToList() ?? throw new DomainException("Orçamento sem itens.");
        if (ordemServicoId == Guid.Empty) throw new DomainException("OS é obrigatória.");
        if (lista.Count == 0) throw new DomainException("Orçamento sem itens.");
        if (lista.Any(i => i.Quantidade <= 0 || i.ValorUnitario < 0)) throw new DomainException("Item de orçamento inválido.");
        var agora = DateTime.UtcNow;
        return new Orcamento
        {
            OrdemServicoId = ordemServicoId,
            ClienteId = clienteId,
            Itens = lista,
            ValorTotal = lista.Sum(i => i.Subtotal),
            Status = StatusOrcamento.Pendente,
            CriadoEm = agora,
            ValidoAte = agora.Add(validade)
        };
    }

    public void Aprovar(DateTimeOffset agora)
    {
        ExigirPendente();
        // ValidoAte é UTC: compara pelo instante (UtcDateTime), nunca pelo relógio local do offset.
        if (agora.UtcDateTime > ValidoAte) throw new DomainException("Orçamento expirado.");
        Status = StatusOrcamento.Aprovado;
    }
    public void Recusar(string motivo)
    {
        ExigirPendente();
        Status = StatusOrcamento.Recusado;
        MotivoRecusa = motivo;
    }

    public void Cancelar()
    {
        if (Status == StatusOrcamento.Cancelado) return;
        Status = StatusOrcamento.Cancelado;
    }

    private void ExigirPendente()
    {
        if (Status != StatusOrcamento.Pendente)
            throw new DomainException($"Orçamento no status {Status} não aceita esta operação.");
    }
}
