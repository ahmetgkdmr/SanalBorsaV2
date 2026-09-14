using Hangfire;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SanalBorsa.API.Security;
using SanalBorsa.Application.Common.Models;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Application.Stocks.Commands.RefreshIntradaySparkline;
using SanalBorsa.Application.Stocks.Commands.SyncUsAdjustedCloses;
using SanalBorsa.Application.Stocks.Commands.SyncUsCorporateActions;
using SanalBorsa.Application.Stocks.Commands.SyncUsDailyPrices;
using SanalBorsa.Application.Stocks.Commands.SyncUsStockUniverse;
using SanalBorsa.Application.Stocks.Queries.CalculateTimeMachine;
using SanalBorsa.Application.Stocks.Queries.GetStockDetail;
using SanalBorsa.Application.Stocks.Queries.GetUsStocks;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Infrastructure.Jobs;

namespace SanalBorsa.API.Controllers;

/// <summary>
/// ABD hisseleri (S&amp;P 500 pilotu) — sadece veri borusu. Portföyde alım/satım henüz yok.
/// Fiyat + kurumsal işlem kaynağı: Yahoo Finance. Aynı Stock/StockPriceHistory/CorporateAction
/// tabloları BIST/Kripto ile paylaşılır, MarketType.UsStocks ile ayrılır.
/// </summary>
[ApiController]
[Route("api/us-stocks")]
[Produces("application/json")]
public class UsStocksController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IBackgroundJobClient _jobs;

    public UsStocksController(IMediator mediator, IBackgroundJobClient jobs)
    {
        _mediator = mediator;
        _jobs = jobs;
    }

    /// <summary>Pilot listesi — 10 hisse, son fiyat + 28 günlük sparkline.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<StockDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] bool? isActive = true, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetUsStocksQuery(isActive), ct);
        return Ok(result);
    }

    /// <summary>Pilot sembollerini (UsStockSymbolSeed) Stock satırına çevirir (idempotent).</summary>
    [AdminApiKey]
    [HttpPost("universe/sync")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public IActionResult SyncUniverse()
    {
        var jobId = _jobs.Enqueue<IMediator>(m => m.Send(new SyncUsStockUniverseCommand(), CancellationToken.None));
        return Accepted(new { message = "ABD hisse evreni sync başladı.", jobId });
    }

    /// <summary>
    /// Günlük OHLC + AdjustedClose senkronu (Yahoo Finance). full=true tüm geçmişi yeniden çeker.
    /// </summary>
    [AdminApiKey]
    [HttpPost("sync-prices")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public IActionResult SyncPrices(
        [FromQuery] bool full = false,
        [FromQuery] string? symbol = null,
        [FromQuery] int? lookbackDays = null)
    {
        var cmd = new SyncUsDailyPricesCommand(full, symbol, lookbackDays);
        var jobId = _jobs.Enqueue<IMediator>(m => m.Send(cmd, CancellationToken.None));

        return Accepted(new
        {
            message = "ABD hisse fiyat sync başladı (Yahoo Finance).",
            full,
            symbol,
            lookbackDays,
            jobId,
        });
    }

    /// <summary>
    /// TÜM ABD fiyat geçmişini (ham + AdjustedClose) Yahoo Finance'e karşı denetler; %2'den fazla
    /// sapan hisseleri TradingView'den yeniden çeker, hâlâ sapıyorsa loglar (manuel inceleme).
    /// Yüzlerce hisse × tam geçmiş olduğu için uzun sürer — arka planda çalışır.
    /// </summary>
    [AdminApiKey]
    [HttpPost("price-audit")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> PriceAudit(
        [FromServices] PriceDataAuditJob job,
        [FromQuery] string? symbol = null,
        [FromQuery] bool sync = false,
        CancellationToken ct = default)
    {
        if (sync)
        {
            await job.RunAsync(MarketType.UsStocks, symbol, ct);
            return Ok(new { message = "ABD fiyat denetimi tamamlandı (senkron) — detaylar loglarda.", symbol });
        }

        var jobId = _jobs.Enqueue<PriceDataAuditJob>(
            j => j.RunAsync(MarketType.UsStocks, symbol, CancellationToken.None));

        return Accepted(new
        {
            message = "ABD fiyat denetimi (Yahoo Finance'e karşı) başladı — arka planda çalışıyor, loglardan takip edilebilir.",
            symbol,
            jobId,
        });
    }

    /// <summary>Temettü + split senkronu (Yahoo Finance). Sadece ekler/dedupe eder, silmez.</summary>
    [AdminApiKey]
    [HttpPost("corporate-actions/sync")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public IActionResult SyncCorporateActions([FromQuery] string? symbol = null)
    {
        var cmd = new SyncUsCorporateActionsCommand(symbol);
        var jobId = _jobs.Enqueue<IMediator>(m => m.Send(cmd, CancellationToken.None));

        return Accepted(new { message = "ABD kurumsal işlem sync başladı (Yahoo Finance).", symbol, jobId });
    }

    /// <summary>
    /// Düzeltilmiş kapanış (split + temettü dahil toplam getiri) senkronu — Zaman Makinesi'nin
    /// para hesabı artık bu seriyi kullanıyor (bkz. TimeMachineCalculator).
    /// </summary>
    [AdminApiKey]
    [HttpPost("adjusted-closes/sync")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(SyncUsAdjustedClosesResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> SyncAdjustedCloses(
        [FromQuery] string? symbol = null,
        [FromQuery] int? lookbackDays = null,
        [FromQuery] bool sync = false,
        CancellationToken ct = default)
    {
        var cmd = new SyncUsAdjustedClosesCommand(symbol, lookbackDays);

        if (sync)
        {
            var result = await _mediator.Send(cmd, ct);
            return Ok(result);
        }

        var jobId = _jobs.Enqueue<IMediator>(m => m.Send(cmd, CancellationToken.None));

        return Accepted(new { message = "ABD düzeltilmiş kapanış sync başladı (TradingView).", symbol, jobId });
    }

    /// <summary>Önceki tam seans gününün 15dk sparkline bar'larını yeniler (normalde 16:05 ET cron'u).</summary>
    [AdminApiKey]
    [HttpPost("intraday-sparkline/sync")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public IActionResult SyncIntradaySparkline()
    {
        var jobId = _jobs.Enqueue<IMediator>(
            m => m.Send(new RefreshIntradaySparklineCommand(MarketType.UsStocks), CancellationToken.None));

        return Accepted(new { message = "ABD intraday sparkline sync başladı.", jobId });
    }

    /// <summary>Hisse detayı — fiyat geçmişi + kurumsal işlemler (piyasa-bağımsız, değişiklik yok).</summary>
    [HttpGet("{symbol}")]
    [ProducesResponseType(typeof(StockDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBySymbol(string symbol, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetStockDetailQuery(symbol, MarketType.UsStocks), ct);
        return Ok(result);
    }

    /// <summary>Zaman makinesi — ABD için amount (USD) zorunlu, asgari ücret varsayımı yok.</summary>
    [HttpGet("{symbol}/time-machine")]
    [ProducesResponseType(typeof(TimeMachineResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> CalculateTimeMachine(
        string symbol,
        [FromQuery] DateTime date,
        [FromQuery] string mode = "lump",
        [FromQuery] decimal? amount = null,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new CalculateTimeMachineQuery(symbol, date, WagePercentage: 0, mode, amount, MarketType.UsStocks),
            ct);
        return Ok(result);
    }
}
