using MediatR;
using Microsoft.AspNetCore.Mvc;
using SanalBorsa.Application.CorporateActions.Queries.GetCorporateActions;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Enums;

namespace SanalBorsa.API.Controllers;

[ApiController]
[Route("api/stocks/{symbol}/corporate-actions")]
[Produces("application/json")]
public class CorporateActionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public CorporateActionsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Returns corporate actions (dividends, bonus issues, rights issues) for a stock.
    /// Optionally filter by action type: 1=RightsIssue, 2=BonusIssue, 3=Dividend.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CorporateActionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        string symbol,
        [FromQuery] CorporateActionType? type = null,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetCorporateActionsQuery(symbol, type), ct);
        return Ok(result);
    }
}
