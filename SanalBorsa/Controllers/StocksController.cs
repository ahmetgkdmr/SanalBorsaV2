using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Models;
using SanalBorsa.Application.Common.Seeds;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Application.Stocks.Commands.BootstrapMarketData;
using SanalBorsa.Application.Stocks.Commands.RefreshStockHistory;
using SanalBorsa.Application.Stocks.Commands.ReplaceStockPriceHistory;
using SanalBorsa.Application.Stocks.Commands.SyncCorporateActions;
using SanalBorsa.Application.Stocks.Commands.SyncStocks;
using SanalBorsa.Application.Stocks.Commands.UpsertStockPriceHistory;
using SanalBorsa.Application.Stocks.Commands.DeleteOldPriceHistories;
using SanalBorsa.Application.Stocks.Commands.SyncStockUniverse;
using SanalBorsa.Application.Stocks.Commands.WipeAllPriceHistories;
using SanalBorsa.Application.Stocks.Queries.CalculateTimeMachine;
using SanalBorsa.Application.Stocks.Queries.GetAllStocks;
using SanalBorsa.Application.Stocks.Queries.GetStockDetail;
using SanalBorsa.Application.Stocks.Queries.GetTopGainers;
using SanalBorsa.Application.Stocks.Commands.ComputeTopGainers;
using SanalBorsa.Domain.Interfaces;

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
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetAllStocksQuery(page, pageSize, search, isActive, indexFilter), ct);
        return Ok(result);
    }

    /// <summary>Son 1 hafta / 1 ay / 1 yıl dönem şampiyonları (en çok kazanan).</summary>
    [HttpGet("top-gainers")]
    [ProducesResponseType(typeof(TopGainersResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTopGainers(CancellationToken ct = default)
        => Ok(await _mediator.Send(new GetTopGainersQuery(), ct));

    /// <summary>Dönem şampiyonlarını yeniden hesapla (manuel tetik).</summary>
    [HttpPost("top-gainers/compute")]
    [ProducesResponseType(typeof(ComputeTopGainersResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ComputeTopGainers(CancellationToken ct = default)
        => Ok(await _mediator.Send(new ComputeTopGainersCommand(), ct));

    /// <summary>
    /// Lightweight symbol list for external sync scripts (TradingView import etc).
    /// </summary>
    [HttpGet("symbols")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSymbols(
        [FromServices] IUnitOfWork uow,
        [FromQuery] bool includeInstruments = false,
        CancellationToken ct = default)
    {
        var stocks = await uow.Stocks.GetAllActiveAsync(ct);
        var items = stocks
            .Where(s => includeInstruments || !MarketInstrumentSeed.IsMarketInstrument(s.Exchange))
            .OrderBy(s => s.Symbol)
            .Select(s => new
            {
                s.Id,
                s.Symbol,
                s.Name,
                s.Exchange,
                s.EarliestDataDate,
                s.LatestDataDate,
                s.NeedsHistoryRefresh,
            })
            .ToList();

        return Ok(new { total = items.Count, items });
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
            ? Domain.Entities.MarketType.Crypto
            : Domain.Entities.MarketType.Bist;

        var result = await _mediator.Send(
            new CalculateTimeMachineQuery(symbol, date, pct, mode, amount, mt),
            ct);
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
    /// Syncs bedelli / bedelsiz / nakit temettü (+ rüçhan fiyatı when available).
    /// full=true: İş Yatırım import for every stock (~30–60 min).
    /// resume=true with full: no wipe; skip stocks that already have rows (continue after crash).
    /// full=false: incremental KAP (nightly) — new events after latest DB date.
    /// Always runs in background.
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
    /// Deletes ALL rows in StockPriceHistories and resets earliest/latest dates on stocks.
    /// Used before a full TradingView re-import.
    /// </summary>
    [HttpDelete("price-histories")]
    [ProducesResponseType(typeof(WipeAllPriceHistoriesResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> WipeAllPriceHistories(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new WipeAllPriceHistoriesCommand(), ct);
        return Ok(result);
    }

    /// <summary>
    /// Deletes price rows with CreatedAt &lt; before (UTC). Default: 2026-07-15 — drops non-TradingView leftovers.
    /// Does not touch TradingView imports from that day onward.
    /// </summary>
    [HttpDelete("price-histories/old")]
    [ProducesResponseType(typeof(DeleteOldPriceHistoriesResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteOldPriceHistories(
        [FromQuery] DateTime? before = null,
        CancellationToken ct = default)
    {
        var cutoff = before?.ToUniversalTime()
                     ?? new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc);
        if (cutoff.Kind == DateTimeKind.Unspecified)
            cutoff = DateTime.SpecifyKind(cutoff, DateTimeKind.Utc);

        var result = await _mediator.Send(new DeleteOldPriceHistoriesCommand(cutoff), ct);
        return Ok(result);
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

    /// <summary>
    /// Replaces the entire price history for one symbol with the provided daily bars (typically TradingView ham export).
    /// </summary>
    [HttpPut("{symbol}/price-histories")]
    [ProducesResponseType(typeof(ReplaceStockPriceHistoryResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReplacePriceHistory(
        string symbol,
        [FromBody] ReplacePriceHistoryRequest body,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new ReplaceStockPriceHistoryCommand(symbol, body.Bars, body.Source),
            ct);

        if (result.Error is not null && result.Error.StartsWith("Symbol not found", StringComparison.Ordinal))
            return NotFound(result);

        return Ok(result);
    }

    /// <summary>
    /// Upserts daily bars (TradingView incremental). Existing dates in the payload range are replaced;
    /// older history outside the range is kept.
    /// </summary>
    [HttpPost("{symbol}/price-histories/upsert")]
    [ProducesResponseType(typeof(UpsertStockPriceHistoryResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpsertPriceHistory(
        string symbol,
        [FromBody] ReplacePriceHistoryRequest body,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new UpsertStockPriceHistoryCommand(symbol, body.Bars, body.Source),
            ct);

        if (result.Error is not null && result.Error.StartsWith("Symbol not found", StringComparison.Ordinal))
            return NotFound(result);

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

public record ReplacePriceHistoryRequest(
    IReadOnlyList<PriceBarDto> Bars,
    string? Source = "tradingview");

public record SyncStockUniverseRequest(
    IReadOnlyList<UniverseStockDto>? Add = null,
    IReadOnlyList<string>? Remove = null);
