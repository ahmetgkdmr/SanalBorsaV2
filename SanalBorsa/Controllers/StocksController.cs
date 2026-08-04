using Hangfire;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SanalBorsa.Application.Common.Models;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Application.Stocks.Commands.BootstrapMarketData;
using SanalBorsa.Application.Stocks.Commands.ComputeTopGainers;
using SanalBorsa.Application.Stocks.Commands.SyncBistAdjustedCloses;
using SanalBorsa.Application.Stocks.Commands.SyncBistDailyPrices;
using SanalBorsa.Application.Stocks.Commands.SyncCorporateActions;
using SanalBorsa.Application.Stocks.Commands.SyncStockUniverse;
using SanalBorsa.Application.Stocks.Commands.SyncStocks;
using SanalBorsa.Application.Stocks.Queries.CalculateTimeMachine;
using SanalBorsa.Application.Stocks.Queries.GetAllStocks;
using SanalBorsa.Application.Stocks.Queries.GetStockDetail;
using SanalBorsa.Application.Stocks.Queries.GetTopGainers;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Infrastructure.Jobs;

namespace SanalBorsa.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class StocksController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IBackgroundJobClient _jobs;

    public StocksController(IMediator mediator, IBackgroundJobClient jobs)
    {
        _mediator = mediator;
        _jobs = jobs;
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
    public IActionResult ComputeTopGainers([FromQuery] string? marketType = null)
    {
        var mt = ParseMarketType(marketType);
        var jobId = _jobs.Enqueue<IMediator>(m => m.Send(new ComputeTopGainersCommand(mt), CancellationToken.None));

        return Accepted(new { message = "Top gainers compute started.", marketType = mt.ToString(), jobId });
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
    public IActionResult Bootstrap()
    {
        var jobId = _jobs.Enqueue<IMediator>(m => m.Send(new BootstrapMarketDataCommand(), CancellationToken.None));
        return Accepted(new { message = "Market bootstrap started in background.", jobId });
    }

    /// <summary>
    /// BIST ham günlük fiyat sync (TradingView WebSocket, adjustment=none).
    /// full=true tüm geçmişi yeniden çeker. Arka planda çalışır.
    /// </summary>
    [HttpPost("sync-prices")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public IActionResult SyncBistPrices(
        [FromQuery] bool full = false,
        [FromQuery] string? symbol = null,
        [FromQuery] int? lookbackDays = null)
    {
        var jobId = _jobs.Enqueue<IMediator>(
            m => m.Send(new SyncBistDailyPricesCommand(full, symbol, lookbackDays), CancellationToken.None));

        return Accepted(new
        {
            message = "BIST ham fiyat sync başladı (TradingView WebSocket).",
            full,
            symbol,
            lookbackDays,
            jobId,
        });
    }

    /// <summary>
    /// Mevcut satırlarda yalnızca AdjustedClose günceller (TV adjustment=dividends).
    /// Close / OHLCV değişmez. Arka planda çalışır.
    /// </summary>
    [HttpPost("sync-adjusted-closes")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public IActionResult SyncAdjustedCloses(
        [FromQuery] string? symbol = null,
        [FromQuery] int? lookbackDays = null)
    {
        var cmd = new SyncBistAdjustedClosesCommand(Symbol: symbol, LookbackDays: lookbackDays);
        var jobId = _jobs.Enqueue<IMediator>(m => m.Send(cmd, CancellationToken.None));

        return Accepted(new
        {
            message = "BIST AdjustedClose sync başladı (TradingView dividends).",
            symbol,
            lookbackDays,
            jobId,
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
        [FromQuery] bool full = false,
        [FromQuery] bool resume = false)
    {
        var cmd = new SyncCorporateActionsCommand(FullResync: full, Resume: resume);
        var jobId = _jobs.Enqueue<IMediator>(m => m.Send(cmd, CancellationToken.None));

        return Accepted(new
        {
            message = full
                ? (resume
                    ? "Resume İş Yatırım corporate-action import started (no wipe)."
                    : "Full wipe + İş Yatırım corporate-action import started in background.")
                : "Incremental KAP corporate-action sync started in background.",
            full,
            resume,
            source = full ? "IsYatirim" : "KAP",
            jobId,
        });
    }

    /// <summary>
    /// Eksik BIST sembollerini ekler / yeniden aktif eder; Remove listesini soft-pasife çeker.
    /// Fiyat geçmişi silinmez. Market instruments (INDEX/FX) dokunulmaz.
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
    /// TradingView'den son N günde bar alınamayan aktif BIST hisselerini soft-pasife çeker.
    /// Fiyat geçmişi korunur. Arka planda çalışır.
    /// </summary>
    [HttpPost("deactivate-inactive")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public IActionResult DeactivateInactive([FromQuery] int lookbackDays = 60)
    {
        var jobId = _jobs.Enqueue<DeactivateInactiveBistStocksJob>(
            j => j.RunAsync(lookbackDays, CancellationToken.None));

        return Accepted(new
        {
            message = "BIST inactive soft-deactivate başladı (TV boş → IsActive=false, fiyatlar silinmez).",
            lookbackDays,
            jobId,
        });
    }
}

public record SyncStockUniverseRequest(
    IReadOnlyList<UniverseStockDto>? Add = null,
    IReadOnlyList<string>? Remove = null);
