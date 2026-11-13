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
    public PagamentosController(OrcamentoUseCases uc) => _uc = uc;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PagamentoResponse>> Obter(Guid id, CancellationToken ct) => Ok(await _uc.ObterPagamentoAsync(id, ct));
}
