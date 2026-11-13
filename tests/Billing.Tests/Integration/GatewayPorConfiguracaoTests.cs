using Billing.Application.Pagamentos;
using Billing.Infrastructure;
using Billing.Infrastructure.MercadoPago;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Billing.Tests.Integration;

public class GatewayPorConfiguracaoTests
{
    private static ServiceProvider Montar(string? modo, string? baseUrl = null)
    {
        var dados = new Dictionary<string, string?>();
        if (modo is not null) dados["MercadoPago:Modo"] = modo;
        if (baseUrl is not null) dados["MercadoPago:BaseUrl"] = baseUrl;
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(dados).Build();
        return new ServiceCollection().AddLogging().AddGatewayPagamento(cfg).BuildServiceProvider();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Fake")]
    public void Fake_e_o_padrao(string? modo)
        => Montar(modo).GetRequiredService<IGatewayPagamento>().Should().BeOfType<FakeGatewayPagamento>();

    [Fact]
    public void Real_usa_o_gateway_HTTP_com_BaseAddress_da_configuracao()
    {
        using var sp = Montar("real", "https://mp.exemplo.test");

        var gw = sp.GetRequiredService<IGatewayPagamento>();

        gw.Should().BeOfType<MercadoPagoGateway>();
        var http = (HttpClient)typeof(MercadoPagoGateway).GetField("_http", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(gw)!;
        http.BaseAddress.Should().Be(new Uri("https://mp.exemplo.test"));
    }
}