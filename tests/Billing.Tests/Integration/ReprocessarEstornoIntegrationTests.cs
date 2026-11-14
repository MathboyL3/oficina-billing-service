using System.Net;
using System.Net.Http.Headers;
using Billing.Application.Pagamentos;
using Billing.Domain.Pagamentos;
using FluentAssertions;
using Moq;

namespace Billing.Tests.Integration;

public class ReprocessarEstornoIntegrationTests : IClassFixture<BillingApiFactory>
{
    private readonly BillingApiFactory _f;
    public ReprocessarEstornoIntegrationTests(BillingApiFactory f) => _f = f;

    private HttpClient Cliente(string role)
    {
        var c = _f.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", BillingApiFactory.Token(role));
        return c;
    }

    private async Task<Pagamento> PagamentoAsync(bool estornoPendente)
    {
        var p = Pagamento.Criar(Guid.NewGuid(), Guid.NewGuid(), 100m);
        p.Aprovar("pm-" + p.Id);
        if (estornoPendente) p.MarcarEstornoPendente();
        await _f.Pagamentos.InserirAsync(p);
        return p;
    }

    [Fact]
    public async Task Gerente_reprocessa_EstornoPendente_200_e_vira_Estornado()
    {
        var p = await PagamentoAsync(true);
        _f.Gateway.Setup(g => g.EstornarAsync(p.PaymentIdMp!, null, It.IsAny<CancellationToken>())).ReturnsAsync(new EstornoResult(true, null));

        var r = await Cliente("Gerente").PostAsync($"/api/pagamentos/{p.Id}/reprocessar-estorno", null);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        p.Status.Should().Be(StatusPagamento.Estornado);
    }

    [Fact]
    public async Task Pagamento_fora_de_EstornoPendente_409()
    {
        var p = await PagamentoAsync(false);

        var r = await Cliente("Gerente").PostAsync($"/api/pagamentos/{p.Id}/reprocessar-estorno", null);

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Id_inexistente_404()
    {
        var r = await Cliente("Gerente").PostAsync($"/api/pagamentos/{Guid.NewGuid()}/reprocessar-estorno", null);

        r.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Atendente_403()
    {
        var p = await PagamentoAsync(true);

        var r = await Cliente("Atendente").PostAsync($"/api/pagamentos/{p.Id}/reprocessar-estorno", null);

        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Cliente_403()
    {
        var p = await PagamentoAsync(true);

        var r = await Cliente("Cliente").PostAsync($"/api/pagamentos/{p.Id}/reprocessar-estorno", null);

        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}