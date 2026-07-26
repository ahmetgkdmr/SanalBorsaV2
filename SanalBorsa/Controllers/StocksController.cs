using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Models;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Application.Stocks.Commands.BootstrapMarketData;
using SanalBorsa.Application.Stocks.Commands.ComputeTopGainers;
using SanalBorsa.Application.Stocks.Commands.SyncBistDailyPrices;
using SanalBorsa.Application.Stocks.Commands.SyncCorporateActions;
using SanalBorsa.Application.Stocks.Commands.SyncStockUniverse;
using SanalBorsa.Application.Stocks.Commands.SyncStocks;
using SanalBorsa.Application.Stocks.Queries.CalculateTimeMachine;
using SanalBorsa.Application.Stocks.Queries.GetAllStocks;
using SanalBorsa.Application.Stocks.Queries.GetStockDetail;
using SanalBorsa.Application.Stocks.Queries.GetTopGainers;
using SanalBorsa.Domain.Entities;

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
        [FromQuery] string? indexFilter = null,
        [FromQuery] string sortBy = "volume",
        [FromQuery] bool sortDesc = true,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new GetAllStocksQuery(page, pageSize, search, isActive, indexFilter, sortBy, sortDesc),
            ct);
        return Ok(result);
    }

    /// <summary>Period champions (1w / 1m / 1y / 5y / 10y).</summary>
    [HttpGet("top-gainers")]
    [ProducesResponseType(typeof(TopGainersResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTopGainers(
        [FromQuery] string? marketType = null,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetTopGainersQuery(ParseMarketType(marketType)), ct);
        return Ok(result);
    }

    /// <summary>Recompute top gainers table (admin).</summary>
    [HttpPost("top-gainers/compute")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public IActionResult ComputeTopGainers(
        [FromServices] IServiceScopeFactory scopeFactory,
        [FromQuery] string? marketType = null)
    {
        var mt = ParseMarketType(marketType);
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                await mediator.Send(new ComputeTopGainersCommand(mt));
            }
            catch (Exception ex)
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<StocksController>>();
                logger.LogError(ex, "Background top-gainers compute failed");
            }
        });

        return Accepted(new { message = "Top gainers compute started.", marketType = mt.ToString() });
    }

    private static MarketType ParseMarketType(string? value)
        => (value ?? "bist").Trim().ToLowerInvariant() switch
        {
            "crypto" => MarketType.Crypto,
            _ => MarketType.Bist,
        };

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
    /// Simulates an investment from a historical date using real price history and corporate actions.
    /// </summary>
    [HttpGet("{symbol}/time-machine")]
    [ProducesResponseType(typeof(TimeMachineResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CalculateTimeMachine(
        string symbol,
        [FromQuery] DateTime date,
        [FromQuery] decimal pct = 50,
        [FromQuery] string mode = "lump",
        [FromQuery] decimal? amount = null,
        [FromQuery] string? marketType = null,
        CancellationToken ct = default)
    {
        var mt = string.Equals(marketType, "crypto", StringComparison.OrdinalIgnoreCase)
            ? MarketType.Crypto
            : MarketType.Bist;

        var result = await _mediator.Send(
            new CalculateTimeMachineQuery(symbol, date, pct, mode, amount, mt),
            ct);
        return Ok(result);
    }

    /// <summary>
    /// Bootstrap: seeds missing BIST symbols, then fetches price history / corporate actions
    /// for stocks that still need data. Runs in background.
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

        return Accepted(new { message = "Market bootstrap started in background." });
    }

    /// <summary>
    /// BIST ham günlük fiyat sync (TradingView WebSocket, adjustment=none).
    /// full=true tüm geçmişi yeniden çeker. Arka planda çalışır.
    /// </summary>
    [HttpPost("sync-prices")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public IActionResult SyncBistPrices(
        [FromServices] IServiceScopeFactory scopeFactory,
        [FromQuery] bool full = false,
        [FromQuery] string? symbol = null)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                var result = await mediator.Send(new SyncBistDailyPricesCommand(full, symbol));
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<StocksController>>();
                logger.LogInformation(
                    "BIST price sync finished — attempted={A} synced={S} bars={B} failed={F} max={Max:yyyy-MM-dd}",
                    result.Attempted, result.Synced, result.BarsUpserted, result.Failed, result.MaxLatestDate);
            }
            catch (Exception ex)
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<StocksController>>();
                logger.LogError(ex, "Background BIST price sync failed");
            }
        });

        return Accepted(new
        {
            message = "BIST ham fiyat sync başladı (TradingView WebSocket).",
            full,
            symbol,
        });
    }

    /// <summary>Metadata sync (isim/sektör vb.) — fiyat çekmez.</summary>
    [HttpPost("sync")]
    [ProducesResponseType(typeof(SyncStocksResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Sync(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new SyncStocksCommand(), ct);
        return Ok(result);
    }

    /// <summary>
    /// Syncs bedelli / bedelsiz / nakit temettü.
    /// full=true: İş Yatırım (~30–60 dk). full=false: KAP incremental.
    /// </summary>
    [HttpPost("corporate-actions/sync")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public IActionResult SyncCorporateActions(
        [FromServices] IServiceScopeFactory scopeFactory,
        [FromQuery] bool full = false,
        [FromQuery] bool resume = false)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                var result = await mediator.Send(
                    new SyncCorporateActionsCommand(FullResync: full, Resume: resume));
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<StocksController>>();
                logger.LogInformation(
                    "Corporate-action sync finished — processed={P} skipped={S} added={A} removed={R} failed={F}",
                    result.StocksProcessed, result.StocksSkipped, result.ActionsAdded,
                    result.ActionsRemoved, result.Failed);
            }
            catch (Exception ex)
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<StocksController>>();
                logger.LogError(ex, "Background corporate-action sync failed");
            }
        });

        return Accepted(new
        {
            message = full
                ? (resume
                    ? "Resume İş Yatırım corporate-action import started (no wipe)."
                    : "Full wipe + İş Yatırım corporate-action import started in background.")
                : "Incremental KAP corporate-action sync started in background.",
            full,
            resume,
            source = full ? "IsYatirim" : "KAP"
        });
    }

    /// <summary>
    /// Adds missing BIST symbols and/or removes obsolete tickers (prices cascade-deleted).
    /// Market instruments (INDEX/FX) are never removed through this endpoint.
    /// </summary>
    [HttpPost("universe/sync")]
    [ProducesResponseType(typeof(SyncStockUniverseResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> SyncUniverse(
        [FromBody] SyncStockUniverseRequest body,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new SyncStockUniverseCommand(body.Add ?? [], body.Remove ?? []),
            ct);
        return Ok(result);
    }
}

public record SyncStockUniverseRequest(
    IReadOnlyList<UniverseStockDto>? Add = null,
    IReadOnlyList<string>? Remove = null);
