using Billing.Domain.Common;
using Billing.Domain.Orcamentos;
using FluentAssertions;

namespace Billing.Tests.Domain;

public class OrcamentoTests
{
    private static Orcamento Novo(TimeSpan? validade = null) => Orcamento.Gerar(Guid.NewGuid(), Guid.NewGuid(),
        new[] { new ItemOrcamentoDominio("Servico", Guid.NewGuid(), "Troca de óleo", 100m, 1), new ItemOrcamentoDominio("Peca", Guid.NewGuid(), "Filtro", 25.5m, 2) },
        validade ?? TimeSpan.FromHours(72));

    [Fact]
    public void Gerar_calcula_total_e_nasce_pendente()
    {
        var o = Novo();
        o.ValorTotal.Should().Be(151m);
        o.Status.Should().Be(StatusOrcamento.Pendente);
        o.ValidoAte.Should().BeCloseTo(DateTime.UtcNow.AddHours(72), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Gerar_sem_itens_ou_quantidade_invalida_lanca()
    {
        FluentActions.Invoking(() => Orcamento.Gerar(Guid.NewGuid(), Guid.NewGuid(), Array.Empty<ItemOrcamentoDominio>(), TimeSpan.FromHours(1)))
            .Should().Throw<DomainException>();
        FluentActions.Invoking(() => Orcamento.Gerar(Guid.NewGuid(), Guid.NewGuid(), new[] { new ItemOrcamentoDominio("Peca", Guid.NewGuid(), "x", 10m, 0) }, TimeSpan.FromHours(1)))
            .Should().Throw<DomainException>();
        FluentActions.Invoking(() => Orcamento.Gerar(Guid.Empty, Guid.NewGuid(), new[] { new ItemOrcamentoDominio("Peca", Guid.NewGuid(), "x", 10m, 1) }, TimeSpan.FromHours(1)))
            .Should().Throw<DomainException>();
    }

    [Fact]
    public void Gerar_com_itens_nulo_lanca_domain_exception()
    {
        FluentActions.Invoking(() => Orcamento.Gerar(Guid.NewGuid(), Guid.NewGuid(), null!, TimeSpan.FromHours(1)))
            .Should().Throw<DomainException>().WithMessage("Orçamento sem itens.");
    }

    // Mesmo instante UTC expresso no offset dado (relogio de parede = UTC + offset).
    private static DateTimeOffset Em(DateTime utc, TimeSpan offset)
        => new(DateTime.SpecifyKind(utc + offset, DateTimeKind.Unspecified), offset);

    [Fact]
    public void Aprovar_considera_offset_do_instante()
    {
        // Mesmos instantes (+-1 min do vencimento) expressos em UTC-3: comparar agora.DateTime
        // (relogio local, sem converter) deslocaria 3h e aceitaria o vencido / recusaria o valido.
        var o = Novo(TimeSpan.FromHours(1));
        var offset = TimeSpan.FromHours(-3);
        var vencido = Em(o.ValidoAte.AddMinutes(1), offset);
        var valido = Em(o.ValidoAte.AddMinutes(-1), offset);

        FluentActions.Invoking(() => o.Aprovar(vencido)).Should().Throw<DomainException>().WithMessage("Orçamento expirado.");
        o.Aprovar(valido);

        o.Status.Should().Be(StatusOrcamento.Aprovado);
    }
    [Fact]
    public void Aprovar_dentro_da_validade_muda_status()
    {
        var o = Novo();
        o.Aprovar(DateTimeOffset.UtcNow);
        o.Status.Should().Be(StatusOrcamento.Aprovado);
    }

    [Fact]
    public void Aprovar_expirado_lanca_e_recusado_nao_aprova()
    {
        var o = Novo(TimeSpan.FromHours(1));
        FluentActions.Invoking(() => o.Aprovar(DateTimeOffset.UtcNow.AddHours(2))).Should().Throw<DomainException>();
        o.Recusar("caro");
        o.Status.Should().Be(StatusOrcamento.Recusado);
        FluentActions.Invoking(() => o.Aprovar(DateTimeOffset.UtcNow)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Cancelar_eh_idempotente()
    {
        var o = Novo();
        o.Cancelar(); o.Cancelar();
        o.Status.Should().Be(StatusOrcamento.Cancelado);
    }

    [Fact]
    public void Cancelar_recusado_vira_cancelado()
    {
        var o = Novo();
        o.Recusar("caro");
        o.Cancelar();
        o.Status.Should().Be(StatusOrcamento.Cancelado);
    }

    [Fact]
    public void Status_do_orcamento_mantem_valores_persistidos()
    {
        // valores gravados no MongoDB: não podem mudar sem migração
        ((int)StatusOrcamento.Pendente).Should().Be(1);
        ((int)StatusOrcamento.Aprovado).Should().Be(2);
        ((int)StatusOrcamento.Recusado).Should().Be(3);
        ((int)StatusOrcamento.Cancelado).Should().Be(4);
    }
}
