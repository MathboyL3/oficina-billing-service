using System.Security.Claims;
using Billing.Application.DTOs;
using Billing.Application.UseCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Billing.Api.Controllers;

[ApiController]
[Authorize(Roles = "Atendente,Gerente,Cliente")]
[Route("api/pagamentos")]
public class PagamentosController : ControllerBase
{
    private readonly OrcamentoUseCases _uc;
    private readonly PagamentoUseCases _pagamentos;
    public PagamentosController(OrcamentoUseCases uc, PagamentoUseCases pagamentos)
    {
        _uc = uc;
        _pagamentos = pagamentos;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PagamentoResponse>> Obter(Guid id, CancellationToken ct) => Ok(await _uc.ObterPagamentoAsync(id, ClienteDoToken(), ct));

    /// <summary>Reprocessa um estorno que ficou em EstornoPendente (só Gerente). Outro status = 409.</summary>
    [HttpPost("{id:guid}/reprocessar-estorno")]
    [Authorize(Roles = "Gerente")]
    public async Task<ActionResult<PagamentoResponse>> ReprocessarEstorno(Guid id, CancellationToken ct) => Ok(await _pagamentos.ReprocessarEstornoAsync(id, ct));

    /// <summary>Cliente só lê o próprio pagamento: devolve o id do token; atendente/gerente passam sem restrição.</summary>
    private Guid? ClienteDoToken()
    {
        if (!User.IsInRole("Cliente")) return null;
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}
