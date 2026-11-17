using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Billing.Application.Pagamentos;
using Billing.Domain.Common;
using Billing.Domain.Orcamentos;
using Billing.Domain.Pagamentos;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace Billing.Tests.Integration;

public sealed class BillingApiFactory : WebApplicationFactory<Program>
{
    public const string Secret = "segredo-de-teste-com-mais-de-32-caracteres!";
    public const string WebhookSecret = "segredo-webhook-de-teste";

    public InMemoryOrcamentoRepository Orcamentos { get; } = new();
    public InMemoryPagamentoRepository Pagamentos { get; } = new();
    public Mock<IGatewayPagamento> Gateway { get; } = new();
    public Mock<IPublishEndpoint> Bus { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:Secret", Secret);
        builder.UseSetting("MercadoPago:Modo", "Fake");
        builder.UseSetting("MercadoPago:WebhookSecret", WebhookSecret);
        builder.ConfigureTestServices(s =>
        {
            s.RemoveAll<IOrcamentoRepository>();
            s.RemoveAll<IPagamentoRepository>();
            s.RemoveAll<IUnitOfWork>();
            s.RemoveAll<IGatewayPagamento>();
            s.RemoveAll<IPublishEndpoint>();
            s.AddSingleton<IOrcamentoRepository>(Orcamentos);
            s.AddSingleton<IPagamentoRepository>(Pagamentos);
            s.AddSingleton<IUnitOfWork, NoOpUnitOfWork>();
            s.AddSingleton(Gateway.Object);
            s.AddSingleton(Bus.Object);
        });
    }

    public static string Token(string role, Guid? sub = null)
    {
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)), SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken("Oficina.Api", "Oficina.Api",
            new[] { new Claim(JwtRegisteredClaimNames.Sub, (sub ?? Guid.NewGuid()).ToString()), new Claim(ClaimTypes.Role, role) },
            DateTime.UtcNow, DateTime.UtcNow.AddMinutes(10), creds);
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}