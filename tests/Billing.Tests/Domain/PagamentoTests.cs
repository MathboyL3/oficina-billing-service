using Billing.Domain.Common;
using Billing.Domain.Pagamentos;
using FluentAssertions;

namespace Billing.Tests.Domain;

public class PagamentoTests
{
    [Fact]
    public void Ciclo_aprovar_estornar()
    {
        var p = Pagamento.Criar(Guid.NewGuid(), Guid.NewGuid(), 151m);
        p.Status.Should().Be(StatusPagamento.Pendente);
        p.RegistrarPreferencia("pref-1", "https://mp/checkout");
        p.Aprovar("123");
        p.Aprovar("123"); // idempotente
        p.PaymentIdMp.Should().Be("123");
        p.MarcarEstornoPendente();
        p.Status.Should().Be(StatusPagamento.EstornoPendente);
        p.MarcarEstornado();
        p.EstaFinal.Should().BeTrue();
    }

    [Fact]
    public void Regras_invalidas_lancam()
    {
        FluentActions.Invoking(() => Pagamento.Criar(Guid.NewGuid(), Guid.NewGuid(), 0m)).Should().Throw<DomainException>();
        var p = Pagamento.Criar(Guid.NewGuid(), Guid.NewGuid(), 10m);
        FluentActions.Invoking(p.MarcarEstornado).Should().Throw<DomainException>();
        FluentActions.Invoking(p.MarcarEstornoPendente).Should().Throw<DomainException>();
        p.Recusar("9");
        FluentActions.Invoking(() => p.Aprovar("9")).Should().Throw<DomainException>();
    }

    [Fact]
    public void Status_do_pagamento_mantem_valores_persistidos()
    {
        // valores gravados no MongoDB: não podem mudar sem migração
        ((int)StatusPagamento.Pendente).Should().Be(1);
        ((int)StatusPagamento.Aprovado).Should().Be(2);
        ((int)StatusPagamento.Recusado).Should().Be(3);
        ((int)StatusPagamento.EstornoPendente).Should().Be(4);
        ((int)StatusPagamento.Estornado).Should().Be(5);
    }
}
