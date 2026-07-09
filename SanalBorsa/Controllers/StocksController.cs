using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Models;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Application.Stocks.Commands.BootstrapMarketData;
using SanalBorsa.Application.Stocks.Commands.RefreshStockHistory;
using SanalBorsa.Application.Stocks.Commands.SyncStocks;
using SanalBorsa.Application.Stocks.Queries.GetAllStocks;
using SanalBorsa.Application.Stocks.Queries.GetStockDetail;

namespace SanalBorsa.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class StocksController : ControllerBase
{
    private readonly IMediator _mediator;

    public StocksController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Returns a paginated list of BIST stocks with optional search and active filter.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<StockDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? search = null,
        [FromQuery] bool? isActive = true,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetAllStocksQuery(page, pageSize, search, isActive), ct);
        return Ok(result);
    }

    /// <summary>Returns full detail for a specific stock including last 30 days of prices and all corporate actions.</summary>
    [HttpGet("{symbol}")]
    [ProducesResponseType(typeof(StockDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBySymbol(string symbol, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetStockDetailQuery(symbol), ct);
        return Ok(result);
    }

    /// <summary>
    /// Bootstrap: seeds missing BIST symbols from KAP, then fetches price history and corporate actions
    /// only for stocks that still need data (NeedsHistoryRefresh = true). Runs in background.
    /// </summary>
    [HttpPost("bootstrap")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public IActionResult Bootstrap([FromServices] IServiceScopeFactory scopeFactory)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                await mediator.Send(new BootstrapMarketDataCommand());
            }
            catch (Exception ex)
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<StocksController>>();
                logger.LogError(ex, "Background bootstrap failed");
            }
        });

        return Accepted(new { message = "Market bootstrap started in background. Monitor logs/sanalborsa-*.log for progress." });
    }

    /// <summary>
    /// Manually triggers a full data sync — fetches latest prices and checks for new corporate actions.
    /// Intended for development/admin use.
    /// </summary>
    [HttpPost("sync")]
    [ProducesResponseType(typeof(SyncStocksResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Sync(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new SyncStocksCommand(), ct);
        return Ok(result);
    }

    /// <summary>
    /// Manually triggers a full history re-fetch for stocks that need it, or for a specific symbol.
    /// </summary>
    [HttpPost("{symbol}/refresh-history")]
    [ProducesResponseType(typeof(RefreshStockHistoryResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RefreshHistory(string symbol, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new RefreshStockHistoryCommand(symbol), ct);
        return Ok(result);
    }
}
