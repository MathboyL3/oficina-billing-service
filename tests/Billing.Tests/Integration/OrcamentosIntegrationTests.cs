using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Billing.Application.DTOs;
using Billing.Domain.Orcamentos;
using Billing.Domain.Pagamentos;
using FluentAssertions;
using Moq;
using Oficina.Contracts;

namespace Billing.Tests.Integration;

public class OrcamentosIntegrationTests : IClassFixture<BillingApiFactory>
{
    private readonly BillingApiFactory _f;
    public OrcamentosIntegrationTests(BillingApiFactory f) => _f = f;

    private HttpClient Client(string role, Guid? sub = null)
    {
        var c = _f.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", BillingApiFactory.Token(role, sub));
        return c;
    }

    private async Task<Orcamento> NovoAsync(Guid cliente)
    {
        var o = Orcamento.Gerar(Guid.NewGuid(), cliente,
            new[] { new ItemOrcamentoDominio("Servico", Guid.NewGuid(), "Troca de óleo", 100m, 1) }, TimeSpan.FromHours(72));
        await _f.Orcamentos.InserirAsync(o);
        return o;
    }

    [Fact]
    public async Task Sem_token_401()
        => (await _f.CreateClient().GetAsync($"/api/orcamentos/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Cliente_dono_aprova_200_e_publica_OrcamentoAprovado()
    {
        var cliente = Guid.NewGuid();
        var o = await NovoAsync(cliente);

        var r = await Client("Cliente", cliente).PostAsync($"/api/orcamentos/{o.Id}/aprovar", null);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await r.Content.ReadFromJsonAsync<OrcamentoResponse>())!.Status.Should().Be("Aprovado");
        _f.Bus.Verify(b => b.Publish(It.Is<OrcamentoAprovado>(m => m.OrcamentoId == o.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Outro_cliente_403()
    {
        var o = await NovoAsync(Guid.NewGuid());

        var r = await Client("Cliente", Guid.NewGuid()).PostAsync($"/api/orcamentos/{o.Id}/aprovar", null);

        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Atendente_recusa_com_motivo_e_consulta_por_OS()
    {
        var o = await NovoAsync(Guid.NewGuid());
        var c = Client("Atendente");

        var r = await c.PostAsJsonAsync($"/api/orcamentos/{o.Id}/recusar", new RecusarOrcamentoRequest("caro"));

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await c.GetFromJsonAsync<List<OrcamentoResponse>>($"/api/orcamentos?osId={o.OrdemServicoId}"))!.Should().ContainSingle(x => x.MotivoRecusa == "caro");
        (await c.GetFromJsonAsync<OrcamentoResponse>($"/api/orcamentos/{o.Id}"))!.Status.Should().Be("Recusado");
    }

    [Fact]
    public async Task Orcamento_inexistente_404_e_aprovar_duas_vezes_422()
    {
        var o = await NovoAsync(Guid.NewGuid());
        var c = Client("Gerente");

        (await c.GetAsync($"/api/orcamentos/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PostAsync($"/api/orcamentos/{o.Id}/aprovar", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await c.PostAsync($"/api/orcamentos/{o.Id}/aprovar", null)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Cliente_dono_le_orcamento_e_lista_por_OS_mas_outro_cliente_recebe_403()
    {
        var dono = Guid.NewGuid();
        var o = await NovoAsync(dono);

        (await Client("Cliente", dono).GetAsync($"/api/orcamentos/{o.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client("Cliente", dono).GetAsync($"/api/orcamentos?osId={o.OrdemServicoId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client("Cliente", Guid.NewGuid()).GetAsync($"/api/orcamentos/{o.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Client("Cliente", Guid.NewGuid()).GetAsync($"/api/orcamentos?osId={o.OrdemServicoId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Client("Gerente").GetAsync($"/api/orcamentos/{o.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client("Atendente").GetAsync($"/api/orcamentos?osId={o.OrdemServicoId}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Pagamento_so_e_lido_pelo_cliente_dono_do_orcamento_e_pela_equipe()
    {
        var dono = Guid.NewGuid();
        var o = await NovoAsync(dono);
        var pag = Pagamento.Criar(o.OrdemServicoId, o.Id, 100m);
        await _f.Pagamentos.InserirAsync(pag);

        (await Client("Cliente", dono).GetAsync($"/api/pagamentos/{pag.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client("Cliente", Guid.NewGuid()).GetAsync($"/api/pagamentos/{pag.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Client("Gerente").GetAsync($"/api/pagamentos/{pag.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Pagamento_de_orcamento_inexistente_e_negado_ao_cliente()
    {
        var pag = Pagamento.Criar(Guid.NewGuid(), Guid.NewGuid(), 100m);
        await _f.Pagamentos.InserirAsync(pag);

        (await Client("Cliente", Guid.NewGuid()).GetAsync($"/api/pagamentos/{pag.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Pagamento_consulta_200_e_404()
    {
        var pag = Pagamento.Criar(Guid.NewGuid(), Guid.NewGuid(), 10m);
        await _f.Pagamentos.InserirAsync(pag);
        var c = Client("Atendente");

        (await c.GetFromJsonAsync<PagamentoResponse>($"/api/pagamentos/{pag.Id}"))!.Status.Should().Be("Pendente");
        (await c.GetAsync($"/api/pagamentos/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Health_live_ok_e_swagger_na_raiz()
    {
        var c = _f.CreateClient();

        (await c.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await c.GetAsync("/swagger/v1/swagger.json");
        doc.StatusCode.Should().Be(HttpStatusCode.OK);
        (await doc.Content.ReadAsStringAsync()).Should().Contain("/api/orcamentos");
    }
}