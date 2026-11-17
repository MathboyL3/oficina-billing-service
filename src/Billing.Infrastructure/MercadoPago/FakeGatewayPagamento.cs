using Billing.Application.Pagamentos;
using Microsoft.Extensions.Options;

namespace Billing.Infrastructure.MercadoPago;

/// <summary>
/// Gateway para testes/demo (<c>MercadoPago:Modo=Fake</c>): confirma na hora, sem webhook e sem rede.
/// Sem estado: o paymentId é <c>fake-{PagamentoId:N}</c> e o status vem só da configuração
/// (aprovar/recusar/falhar estorno), então qualquer réplica ou restart atende qualquer pagamento Fake.
/// O Fake não conhece a OS pelo paymentId: <see cref="PagamentoInfo.ExternalReference"/> vem nulo
/// e o chamador (CriarCobrancaConsumer) a preenche.
/// </summary>
public sealed class FakeGatewayPagamento : IGatewayPagamento
{
    private const string Prefixo = "fake-";
    private readonly MercadoPagoOptions _opt;

    public FakeGatewayPagamento(IOptions<MercadoPagoOptions> opt) => _opt = opt.Value;

    private static bool PaymentIdValido(string paymentId)
        => paymentId.StartsWith(Prefixo, StringComparison.Ordinal) && Guid.TryParseExact(paymentId[Prefixo.Length..], "N", out _);

    public Task<CobrancaCriadaResult> CriarCobrancaAsync(CriarCobrancaRequest r, CancellationToken ct)
        => Task.FromResult(new CobrancaCriadaResult($"fake-pref-{r.PagamentoId}", $"{_opt.PublicBaseUrl.TrimEnd('/')}/fake-checkout/{r.PagamentoId}"));

    public Task<PagamentoInfo> ObterPagamentoAsync(string paymentId, CancellationToken ct)
    {
        if (!PaymentIdValido(paymentId))
            throw new HttpRequestException($"Pagamento {paymentId} não encontrado (404).", null, System.Net.HttpStatusCode.NotFound);
        var status = _opt.FakeComportamento == "Recusar" ? StatusPagamentoGateway.Recusado : StatusPagamentoGateway.Aprovado;
        return Task.FromResult(new PagamentoInfo(paymentId, status, null, status.ToString()));
    }

    /// <summary>Sem estado, o Fake não localiza pagamento por referência.</summary>
    public Task<PagamentoInfo?> BuscarPorReferenciaAsync(string externalReference, CancellationToken ct)
        => Task.FromResult<PagamentoInfo?>(null);

    public Task<EstornoResult> EstornarAsync(string paymentId, decimal? valor, CancellationToken ct)
    {
        if (_opt.FakeComportamento == "FalharEstorno")
            return Task.FromResult(new EstornoResult(false, "Falha simulada de estorno."));
        return Task.FromResult(PaymentIdValido(paymentId) ? new EstornoResult(true, null) : new EstornoResult(false, "404: pagamento não encontrado"));
    }
}