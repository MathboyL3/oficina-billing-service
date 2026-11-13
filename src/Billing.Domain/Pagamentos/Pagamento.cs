using Billing.Domain.Common;

namespace Billing.Domain.Pagamentos;

public sealed class Pagamento
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid OrdemServicoId { get; private set; }
    public Guid OrcamentoId { get; private set; }
    public decimal Valor { get; private set; }
    public StatusPagamento Status { get; private set; }
    public string? PreferenceId { get; private set; }
    public string? LinkPagamento { get; private set; }
    public string? PaymentIdMp { get; private set; }
    public DateTime AtualizadoEm { get; private set; }

    public bool EstaFinal => Status is StatusPagamento.Recusado or StatusPagamento.Estornado;

    private Pagamento() { }

    public static Pagamento Criar(Guid ordemServicoId, Guid orcamentoId, decimal valor)
    {
        if (ordemServicoId == Guid.Empty) throw new DomainException("OS é obrigatória.");
        if (orcamentoId == Guid.Empty) throw new DomainException("Orçamento é obrigatório.");
        if (valor <= 0) throw new DomainException("Valor do pagamento deve ser positivo.");
        return new Pagamento
        {
            OrdemServicoId = ordemServicoId,
            OrcamentoId = orcamentoId,
            Valor = valor,
            Status = StatusPagamento.Pendente,
            AtualizadoEm = DateTime.UtcNow
        };
    }

    public void RegistrarPreferencia(string preferenceId, string link)
    {
        // Reentrega de CriarCobranca não pode trocar o link de uma cobrança já emitida.
        Exigir(StatusPagamento.Pendente);
        if (PreferenceId is not null) throw new DomainException("Pagamento já possui preferência.");
        PreferenceId = preferenceId;
        LinkPagamento = link;
        Tocar();
    }

    public void Aprovar(string paymentIdMp)
    {
        if (Status == StatusPagamento.Aprovado) return;
        Exigir(StatusPagamento.Pendente);
        PaymentIdMp = paymentIdMp;
        Status = StatusPagamento.Aprovado;
        Tocar();
    }

    public void Recusar(string paymentIdMp)
    {
        Exigir(StatusPagamento.Pendente);
        PaymentIdMp = paymentIdMp;
        Status = StatusPagamento.Recusado;
        Tocar();
    }

    public void MarcarEstornoPendente()
    {
        if (Status is not (StatusPagamento.Aprovado or StatusPagamento.EstornoPendente))
            throw new DomainException($"Pagamento no status {Status} não pode ser estornado.");
        Status = StatusPagamento.EstornoPendente;
        Tocar();
    }

    public void MarcarEstornado()
    {
        if (Status is not (StatusPagamento.Aprovado or StatusPagamento.EstornoPendente))
            throw new DomainException($"Pagamento no status {Status} não pode ser estornado.");
        Status = StatusPagamento.Estornado;
        Tocar();
    }

    private void Exigir(StatusPagamento de)
    {
        if (Status != de) throw new DomainException($"Pagamento no status {Status}, esperado {de}.");
    }

    private void Tocar() => AtualizadoEm = DateTime.UtcNow;
}
