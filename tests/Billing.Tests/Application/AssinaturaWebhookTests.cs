using Billing.Application.Pagamentos;
using FluentAssertions;

namespace Billing.Tests.Application;

public class AssinaturaWebhookTests
{
    private const string Secret = "segredo-webhook";

    private static string Header(string dataId, string reqId, long ts, string secret = Secret)
        => $"ts={ts},v1={AssinaturaWebhook.Calcular($"id:{dataId};request-id:{reqId};ts:{ts};", secret)}";

    [Fact]
    public void Assinatura_valida_aceita()
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        AssinaturaWebhook.Valida(Header("123", "req-1", ts), "req-1", "123", Secret).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("lixo")]
    [InlineData("ts=abc,v1=00")]
    [InlineData("v1=deadbeef")]
    public void Header_ausente_ou_malformado_recusa(string? header)
        => AssinaturaWebhook.Valida(header, "req-1", "123", Secret).Should().BeFalse();

    [Fact]
    public void Segredo_errado_ou_dado_trocado_recusa()
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        AssinaturaWebhook.Valida(Header("123", "req-1", ts, "outro"), "req-1", "123", Secret).Should().BeFalse();
        AssinaturaWebhook.Valida(Header("123", "req-1", ts), "req-1", "999", Secret).Should().BeFalse();
        AssinaturaWebhook.Valida(Header("123", "req-1", ts), "req-2", "123", Secret).Should().BeFalse();
    }

    [Fact]
    public void Timestamp_fora_da_tolerancia_recusa()
    {
        var velho = DateTimeOffset.UtcNow.AddMinutes(-30).ToUnixTimeMilliseconds();
        AssinaturaWebhook.Valida(Header("123", "req-1", velho), "req-1", "123", Secret).Should().BeFalse();
    }

    [Fact]
    public void Aceita_ts_em_segundos_e_dataId_maiusculo()
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        AssinaturaWebhook.Valida(Header("abc", "r", ts), "r", "ABC", Secret).Should().BeTrue();
    }

    [Fact]
    public void Segredo_vazio_recusa_sempre()
        => AssinaturaWebhook.Valida("ts=1,v1=aa", "r", "1", "").Should().BeFalse();
}