using MediatR;
using Microsoft.AspNetCore.Mvc;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Application.Indices.Commands.BootstrapMarketIndices;
using SanalBorsa.Application.Indices.Queries.GetIndexQuotes;

namespace SanalBorsa.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class IndicesController : ControllerBase
{
    private readonly IMediator _mediator;

    public IndicesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Returns BIST indices and USD/TRY with latest close and daily change.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<IndexQuoteDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetQuotes(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetIndexQuotesQuery(), ct);
        return Ok(result);
    }

    /// <summary>Seeds and fetches historical data for market instruments (indices + USD/TRY).</summary>
    [HttpPost("bootstrap")]
    [ProducesResponseType(typeof(BootstrapMarketIndicesResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Bootstrap(CancellationToken ct)
    {
        var result = await _mediator.Send(new BootstrapMarketIndicesCommand(), ct);
        return Ok(result);
    }
}
