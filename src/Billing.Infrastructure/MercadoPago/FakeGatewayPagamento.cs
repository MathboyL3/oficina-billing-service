using System.Collections.Concurrent;
using Billing.Application.Pagamentos;
using Microsoft.Extensions.Options;

namespace Billing.Infrastructure.MercadoPago;

/// <summary>
/// Gateway em memória para testes/demo (<c>MercadoPago:Modo=Fake</c>): confirma na hora, sem
/// webhook e sem rede. O comportamento (aprovar/recusar/falhar estorno) vem da configuração.
/// </summary>
public sealed class FakeGatewayPagamento : IGatewayPagamento
{
    private static readonly ConcurrentDictionary<string, PagamentoInfo> PorReferencia = new();
    private readonly MercadoPagoOptions _opt;

    public FakeGatewayPagamento(IOptions<MercadoPagoOptions> opt) => _opt = opt.Value;

    public Task<CobrancaCriadaResult> CriarCobrancaAsync(CriarCobrancaRequest r, CancellationToken ct)
    {
        var referencia = r.PagamentoId.ToString();
        var status = _opt.FakeComportamento == "Recusar" ? StatusPagamentoGateway.Recusado : StatusPagamentoGateway.Aprovado;
        PorReferencia[referencia] = new PagamentoInfo($"fake-{r.PagamentoId:N}", status, referencia, status.ToString());
        return Task.FromResult(new CobrancaCriadaResult($"fake-pref-{r.PagamentoId}", $"{_opt.PublicBaseUrl.TrimEnd('/')}/fake-checkout/{r.PagamentoId}"));
    }

    public Task<PagamentoInfo> ObterPagamentoAsync(string paymentId, CancellationToken ct)
    {
        var info = PorReferencia.Values.FirstOrDefault(p => p.PaymentId == paymentId)
            ?? throw new HttpRequestException($"Pagamento {paymentId} não encontrado (404).", null, System.Net.HttpStatusCode.NotFound);
        return Task.FromResult(info);
    }

    public Task<PagamentoInfo?> BuscarPorReferenciaAsync(string externalReference, CancellationToken ct)
        => Task.FromResult(PorReferencia.TryGetValue(externalReference, out var p) ? p : null);

    public Task<EstornoResult> EstornarAsync(string paymentId, decimal? valor, CancellationToken ct)
    {
        if (_opt.FakeComportamento == "FalharEstorno")
            return Task.FromResult(new EstornoResult(false, "Falha simulada de estorno."));
        var par = PorReferencia.FirstOrDefault(kv => kv.Value.PaymentId == paymentId);
        if (par.Key is not null)
            PorReferencia[par.Key] = par.Value with { Status = StatusPagamentoGateway.Estornado, StatusBruto = "refunded" };
        return Task.FromResult(new EstornoResult(true, null));
    }
}
