using MediatR;
using Microsoft.AspNetCore.Mvc;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Application.PriceHistories.Queries.GetPriceHistory;

namespace SanalBorsa.API.Controllers;

[ApiController]
[Route("api/stocks/{symbol}/price-history")]
[Produces("application/json")]
public class PriceHistoriesController : ControllerBase
{
    private readonly IMediator _mediator;

    public PriceHistoriesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Returns adjusted OHLCV price history for a stock.
    /// Optionally filter by date range.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PriceHistoryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        string symbol,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetPriceHistoryQuery(symbol, from, to), ct);
        return Ok(result);
    }
}
