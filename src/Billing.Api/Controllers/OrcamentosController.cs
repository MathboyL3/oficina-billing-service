using System.Security.Claims;
using Billing.Application.DTOs;
using Billing.Application.UseCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Billing.Api.Controllers;

[ApiController]
[Authorize(Roles = "Cliente,Atendente,Gerente")]
[Route("api/orcamentos")]
public class OrcamentosController : ControllerBase
{
    private readonly OrcamentoUseCases _uc;
    public OrcamentosController(OrcamentoUseCases uc) => _uc = uc;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrcamentoResponse>> Obter(Guid id, CancellationToken ct) => Ok(await _uc.ObterAsync(id, ct));

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OrcamentoResponse>>> ListarPorOs([FromQuery] Guid osId, CancellationToken ct)
        => Ok(await _uc.ListarPorOsAsync(osId, ct));

    /// <summary>Aprova o orçamento (cliente dono, atendente ou gerente) e publica OrcamentoAprovado.</summary>
    [HttpPost("{id:guid}/aprovar")]
    public async Task<ActionResult<OrcamentoResponse>> Aprovar(Guid id, CancellationToken ct)
        => Ok(await _uc.AprovarAsync(id, ClienteDoToken(), ct));

    [HttpPost("{id:guid}/recusar")]
    public async Task<ActionResult<OrcamentoResponse>> Recusar(Guid id, RecusarOrcamentoRequest req, CancellationToken ct)
        => Ok(await _uc.RecusarAsync(id, req, ClienteDoToken(), ct));

    /// <summary>Cliente só opera no próprio orçamento: devolve o id do token; atendente/gerente passam sem restrição.</summary>
    private Guid? ClienteDoToken()
    {
        if (!User.IsInRole("Cliente")) return null;
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}
