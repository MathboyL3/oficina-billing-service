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

    [Fact]
    public void Aprovar_considera_offset_de_data_local()
    {
        // Datas Kind=Local sao convertidas para UTC antes de comparar com ValidoAte. Os dois instantes
        // ficam a apenas 1 min do vencimento, mas os ticks "locais" sao deslocados pelo offset do fuso:
        // a comparacao crua dos ticks (sem ToUniversalTime) erra um dos dois casos em qualquer fuso != UTC
        // (a leste, aceita o vencido; a oeste, recusa o valido). Em maquina UTC o offset e 0 e a
        // normalizacao e a identidade, entao a regressao e inobservavel ali por definicao.
        var o = Novo(TimeSpan.FromHours(1));
        var vencidoLocal = DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(o.ValidoAte.AddMinutes(1), TimeZoneInfo.Local), DateTimeKind.Local);
        var validoLocal = DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(o.ValidoAte.AddMinutes(-1), TimeZoneInfo.Local), DateTimeKind.Local);

        FluentActions.Invoking(() => o.Aprovar(vencidoLocal)).Should().Throw<DomainException>().WithMessage("Or*expirado.");
        o.Aprovar(validoLocal);

        o.Status.Should().Be(StatusOrcamento.Aprovado);
    }
    [Fact]
    public void Aprovar_dentro_da_validade_muda_status()
    {
        var o = Novo();
        o.Aprovar(DateTime.UtcNow);
        o.Status.Should().Be(StatusOrcamento.Aprovado);
    }

    [Fact]
    public void Aprovar_expirado_lanca_e_recusado_nao_aprova()
    {
        var o = Novo(TimeSpan.FromHours(1));
        FluentActions.Invoking(() => o.Aprovar(DateTime.UtcNow.AddHours(2))).Should().Throw<DomainException>();
        o.Recusar("caro");
        o.Status.Should().Be(StatusOrcamento.Recusado);
        FluentActions.Invoking(() => o.Aprovar(DateTime.UtcNow)).Should().Throw<DomainException>();
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
