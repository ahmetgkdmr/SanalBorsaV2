using Hangfire;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SanalBorsa.API.Security;
using SanalBorsa.Application.Common;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Application.Common.Seeds;
using SanalBorsa.Application.Common.Models;
using SanalBorsa.Application.Common.Services;
using SanalBorsa.Application.Portfolio.Commands.ApplyCorporateActionsToPortfolios;
using SanalBorsa.Domain.Interfaces;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Application.Stocks.Commands.BootstrapMarketData;
using SanalBorsa.Application.Stocks.Commands.ComputeTopGainers;
using SanalBorsa.Application.Stocks.Commands.SyncBistAdjustedCloses;
using SanalBorsa.Application.Stocks.Commands.SyncBistDailyPrices;
using SanalBorsa.Application.Stocks.Commands.RefreshIntradaySparkline;
using SanalBorsa.Application.Stocks.Commands.SyncCorporateActions;
using SanalBorsa.Application.Stocks.Commands.SyncStockUniverse;
using SanalBorsa.Application.Stocks.Commands.SyncStocks;
using SanalBorsa.Application.Stocks.Queries.CalculateTimeMachine;
using SanalBorsa.Application.Stocks.Queries.GetAllStocks;
using SanalBorsa.Application.Stocks.Queries.GetStockDetail;
using SanalBorsa.Application.Stocks.Queries.GetTopGainers;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Enums;
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

    /// <summary>Recompute top gainers table (admin). sync=true: local'de güncel kodla senkron
    /// çalışır (bkz. corporate-actions/sync üstündeki not — aynı local-vs-production gerekçesi).</summary>
    [AdminApiKey]
    [HttpPost("top-gainers/compute")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ComputeTopGainersResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ComputeTopGainers(
        [FromQuery] string? marketType = null,
        [FromQuery] bool sync = false,
        CancellationToken ct = default)
    {
        var mt = ParseMarketType(marketType);
        var cmd = new ComputeTopGainersCommand(mt);

        if (sync)
        {
            var result = await _mediator.Send(cmd, ct);
            return Ok(result);
        }

        var jobId = _jobs.Enqueue<IMediator>(m => m.Send(cmd, CancellationToken.None));

        return Accepted(new { message = "Top gainers compute started.", marketType = mt.ToString(), jobId });
    }

    private static MarketType ParseMarketType(string? value)
        => (value ?? "bist").Trim().ToLowerInvariant() switch
        {
            "crypto" => MarketType.Crypto,
            "us" or "usstocks" => MarketType.UsStocks,
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
        var mt = ParseMarketType(marketType);

        var result = await _mediator.Send(
            new CalculateTimeMachineQuery(symbol, date, pct, mode, amount, mt),
            ct);
        return Ok(result);
    }

    /// <summary>
    /// Bootstrap: seeds missing BIST symbols, then fetches price history / corporate actions
    /// for stocks that still need data. Runs in background.
    /// </summary>
    [AdminApiKey]
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
    [AdminApiKey]
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
    [AdminApiKey]
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

    /// <summary>Önceki tam seans gününün 15dk sparkline bar'larını yeniler (normalde 18:45 TR cron'u).</summary>
    [AdminApiKey]
    [HttpPost("intraday-sparkline/sync")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public IActionResult SyncIntradaySparkline()
    {
        var jobId = _jobs.Enqueue<IMediator>(
            m => m.Send(new RefreshIntradaySparklineCommand(MarketType.Bist), CancellationToken.None));

        return Accepted(new { message = "BIST intraday sparkline sync başladı.", jobId });
    }

    /// <summary>Metadata sync (isim/sektör vb.) — fiyat çekmez.</summary>
    [AdminApiKey]
    [HttpPost("sync")]
    [ProducesResponseType(typeof(SyncStocksResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Sync(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new SyncStocksCommand(), ct);
        return Ok(result);
    }

    /// <summary>
    /// Syncs bedelli / bedelsiz / nakit temettü. full=true: KAP (birincil) + İş Yatırım (tamamlayıcı)
    /// birleşik tam geçmiş doldurma. full=false: KAP artımlı. sync=true: Hangfire kuyruğuna atmadan
    /// isteği işleyen sürecin İÇİNDE senkron çalışır — proje sohbeti: prod ile AYNI DB'yi paylaştığımız
    /// için lokal ortamda Hangfire worker'ı kasıtlı olarak kapalı (bkz. DependencyInjection.cs), yani
    /// sync=false ile kuyruğa atılan bir iş burada değil PRODUCTION'da (muhtemelen eski kodla) çalışır.
    /// Lokal makinede güncel kodla test/çalıştırmak için sync=true kullanılmalı.
    /// </summary>
    [AdminApiKey]
    [HttpPost("corporate-actions/sync")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> SyncCorporateActions(
        [FromQuery] bool full = false,
        [FromQuery] bool resume = false,
        [FromQuery] bool sync = false,
        [FromQuery] bool skipKap = false,
        CancellationToken ct = default)
    {
        var cmd = new SyncCorporateActionsCommand(FullResync: full, Resume: resume, SkipKap: skipKap);

        if (sync)
        {
            var syncResult = await _mediator.Send(cmd, ct);
            return Ok(syncResult);
        }

        var jobId = _jobs.Enqueue<IMediator>(m => m.Send(cmd, CancellationToken.None));

        return Accepted(new
        {
            message = full
                ? (resume
                    ? "Resume KAP+İş Yatırım corporate-action import started (no wipe)."
                    : "Full wipe + KAP+İş Yatırım corporate-action import started in background."
                        + " UYARI: lokal ortamda Hangfire worker kapalı, bu iş production'da çalışır — güncel kod için sync=true kullanın.")
                : "Incremental KAP corporate-action sync started in background."
                    + " UYARI: lokal ortamda Hangfire worker kapalı, bu iş production'da çalışır.",
            full,
            resume,
            source = full ? "KAP+IsYatirim" : "KAP",
            jobId,
        });
    }

    /// <summary>
    /// DB'deki bedelsiz/bedelli/temettü olaylarını, o hisseyi ex-date'te (olay tarihinde) elinde
    /// tutan TÜM kullanıcı portföylerine uygular (proje sohbeti: "önce mantık, sonra bildirim" —
    /// bildirim entegrasyonu henüz yok, bu sadece portföy Cash/Holdings mutasyonunu yapar).
    /// Idempotent: AppliedCorporateActions tablosu sayesinde tekrar çağrılması zarasız, sadece
    /// henüz işlenmemiş (olay, portföy) çiftlerini işler. sync=true: Hangfire'a atmadan senkron
    /// çalışır (lokal ortamda Hangfire worker kapalı olduğu için — bkz. yukarıdaki not).
    /// </summary>
    [AdminApiKey]
    [HttpPost("corporate-actions/apply-to-portfolios")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ApplyCorporateActionsToPortfolios(
        [FromQuery] bool sync = false,
        CancellationToken ct = default)
    {
        var cmd = new ApplyCorporateActionsToPortfoliosCommand();

        if (sync)
        {
            var result = await _mediator.Send(cmd, ct);
            return Ok(result);
        }

        var jobId = _jobs.Enqueue<IMediator>(m => m.Send(cmd, CancellationToken.None));

        return Accepted(new
        {
            message = "Corporate action → portfolio application started in background."
                + " UYARI: lokal ortamda Hangfire worker kapalı, bu iş production'da çalışır — güncel kod için sync=true kullanın.",
            jobId,
        });
    }

    /// <summary>
    /// Eksik BIST sembollerini ekler / yeniden aktif eder; Remove listesini soft-pasife çeker.
    /// Fiyat geçmişi silinmez. Market instruments (INDEX/FX) dokunulmaz.
    /// </summary>
    [AdminApiKey]
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
    /// TÜM BIST fiyat geçmişini (ham split-tutarlılığı + düzeltilmiş fiyatın split'lerde pürüzsüzlüğü)
    /// KENDİ kurumsal olay kayıtlarımıza karşı denetler (bkz. RawPriceActionConsistencyService —
    /// dış kaynağa/Yahoo'ya ihtiyaç yok). Sorun bulunan hisseleri TradingView'den yeniden çeker,
    /// hâlâ sapıyorsa loglar (manuel inceleme). Yüzlerce hisse × tam geçmiş olduğu için uzun sürer.
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
            await job.RunAsync(MarketType.Bist, symbol, ct);
            return Ok(new { message = "BIST fiyat denetimi tamamlandı (senkron) — detaylar loglarda.", symbol });
        }

        var jobId = _jobs.Enqueue<PriceDataAuditJob>(
            j => j.RunAsync(MarketType.Bist, symbol, CancellationToken.None));

        return Accepted(new
        {
            message = "BIST fiyat denetimi (kendi kurumsal olay kayıtlarımıza karşı) başladı — arka planda çalışıyor, loglardan takip edilebilir.",
            symbol,
            jobId,
        });
    }

    /// <summary>
    /// Her hisse için en eski günden bugüne, SADECE bizim veritabanımızdaki ham fiyat + kurumsal
    /// olay kayıtlarını (temettü/bedelsiz/bedelli) uygulayarak kendi düzeltilmiş fiyat serimizi
    /// baştan hesaplar ve TradingView'in AdjustedClose'uyla gün gün karşılaştırır (bkz.
    /// IndependentAdjustmentAuditService). %1'den fazla sapan gün varsa loglar. Dış kaynağa
    /// (Yahoo) hiç ihtiyaç yok — tamamen kendi verimizin iç tutarlılığı.
    /// </summary>
    [AdminApiKey]
    [HttpPost("independent-adjustment-audit")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> IndependentAdjustmentAudit(
        [FromServices] IndependentAdjustmentAuditJob job,
        [FromQuery] string? symbol = null,
        [FromQuery] bool sync = false,
        CancellationToken ct = default)
    {
        if (sync)
        {
            await job.RunAsync(symbol, ct);
            return Ok(new { message = "Bağımsız düzeltme denetimi tamamlandı (senkron) — detaylar loglarda.", symbol });
        }

        var jobId = _jobs.Enqueue<IndependentAdjustmentAuditJob>(j => j.RunAsync(symbol, CancellationToken.None));

        return Accepted(new
        {
            message = "Bağımsız düzeltme denetimi (kendi kurumsal olay kayıtlarımıza karşı, en baştan hesaplanan seri) başladı.",
            symbol,
            jobId,
        });
    }

    /// <summary>
    /// TANI AMAÇLI, DB'ye HİÇ YAZMAZ: bir sembolün TÜM kurumsal olay geçmişini KAP (birincil) + İş
    /// Yatırım'dan (tamamlayıcı — proje sohbeti: KAP 2010 öncesine/bazı olaylara hiç gitmiyor) taze
    /// çekip AYNI üretim mantığıyla (CorporateActionMerge.MergePreferKap) birleştirir, DB'deki ham
    /// fiyata uygulayıp geriye doğru kendi düzeltilmiş serimizi hesaplar ve TradingView'in
    /// AdjustedClose'uyla karşılaştırır — DB'yi hiç değiştirmeden gerçek senkronun ne üreteceğini test eder.
    /// </summary>
    [HttpGet("{symbol}/kap-fresh-audit")]
    public async Task<IActionResult> KapFreshAudit(
        string symbol,
        [FromServices] IKapCorporateActionService kap,
        [FromServices] IIsYatirimCorporateActionService isYatirim,
        [FromServices] IUnitOfWork uow,
        [FromServices] IndependentAdjustmentAuditService independentAuditor,
        [FromServices] TvImpliedFactorAuditService impliedAuditor,
        CancellationToken ct)
    {
        symbol = symbol.Trim().ToUpperInvariant();
        var stock = await uow.Stocks.GetBySymbolAsync(symbol, ct);
        if (stock is null)
            return NotFound(new { message = $"{symbol} bulunamadı." });

        var kapActions = await kap.GetCorporateActionsAsync(symbol, sinceDate: null, ct);
        var isYatirimActions = await isYatirim.GetCorporateActionsAsync(symbol, ct);
        var freshKapActions = CorporateActionMerge.MergePreferKap(kapActions, isYatirimActions);
        var prices = await uow.PriceHistories.GetByStockIdAsync(stock.Id, ct: ct);

        var independentResult = independentAuditor.Audit(symbol, prices, freshKapActions);
        var impliedResult = impliedAuditor.Audit(symbol, prices, freshKapActions);

        return Ok(new
        {
            symbol,
            kapActionCount = kapActions.Count,
            isYatirimActionCount = isYatirimActions.Count,
            freshKapActionCount = freshKapActions.Count,
            freshKapActions = freshKapActions.Select(a => new
            {
                a.ActionType, a.ActionDate, a.Value, a.SubscriptionPrice, a.Description
            }),
            independent = new
            {
                independentResult.DaysCompared,
                mismatchCount = independentResult.Mismatches.Count,
                sampleMismatches = independentResult.Mismatches.Take(10).Select(m => new
                {
                    m.Date, m.Computed, m.Actual, m.RatioDiff
                })
            },
            implied = new
            {
                impliedResult.HasIssues,
                missingSteps = impliedResult.MissingSteps,
                magnitudeMismatches = impliedResult.MagnitudeMismatches,
                unexplainedSteps = impliedResult.UnexplainedSteps
            }
        });
    }

    /// <summary>
    /// TANI AMAÇLI, DB'ye HİÇ YAZMAZ: TÜM aktif BIST hisselerinin ham (Close) fiyat serisini tarar,
    /// ardışık günlerde fiyatın HİÇ değişmediği ("düz") bölgeleri bulur — proje sohbeti: ADEL'de
    /// 2012-10-16 BIST halt günü (hacim=0, fiyat bir önceki günle aynı) TV'nin implied factor
    /// hesabında sahte bir sıçrama üretmişti (bkz. TvImpliedFactorAuditService'teki Volume>0 filtresi).
    /// Bu tarama, aynı riski taşıyan (özellikle kurumsal olay tarihine yakın düşen) düz bölgeleri
    /// tüm hisselerde tek seferde bulmak için — bir sonraki halt/veri donması vakasını önceden yakalar.
    /// </summary>
    [HttpGet("flat-price-scan")]
    public async Task<IActionResult> FlatPriceScan(
        [FromServices] IUnitOfWork uow,
        [FromQuery] int minRunDays = 2,
        [FromQuery] int nearActionWindowDays = 5,
        CancellationToken ct = default)
    {
        var stocks = (await uow.Stocks.GetAllActiveAsync(ct, MarketType.Bist))
            .Where(s => !MarketInstrumentSeed.IsMarketInstrument(s.Exchange))
            .OrderBy(s => s.Symbol)
            .ToList();

        var flatRuns = new List<object>();

        foreach (var stock in stocks)
        {
            var prices = (await uow.PriceHistories.GetByStockIdAsync(stock.Id, ct: ct))
                .Where(p => p.Close > 0m)
                .OrderBy(p => p.Date)
                .ToList();
            if (prices.Count < 2) continue;

            var actions = await uow.CorporateActions.GetByStockIdAsync(stock.Id, ct);

            int runStart = 0;
            for (int i = 1; i <= prices.Count; i++)
            {
                bool sameAsPrev = i < prices.Count && prices[i].Close == prices[i - 1].Close;
                if (!sameAsPrev)
                {
                    int runLen = i - runStart;
                    if (runLen >= minRunDays)
                    {
                        var fromDate = prices[runStart].Date;
                        var toDate = prices[i - 1].Date;
                        var totalVolume = prices.Skip(runStart).Take(runLen).Sum(p => p.Volume);
                        bool nearAction = actions.Any(a =>
                            Math.Abs((a.ActionDate - fromDate).TotalDays) <= nearActionWindowDays ||
                            Math.Abs((a.ActionDate - toDate).TotalDays) <= nearActionWindowDays);

                        flatRuns.Add(new
                        {
                            symbol = stock.Symbol,
                            from = fromDate,
                            to = toDate,
                            days = runLen,
                            close = prices[runStart].Close,
                            totalVolume,
                            nearCorporateAction = nearAction
                        });
                    }
                    runStart = i;
                }
            }
        }

        var ordered = flatRuns
            .OrderByDescending(r => ((dynamic)r).nearCorporateAction)
            .ThenByDescending(r => ((dynamic)r).days)
            .ToList();

        return Ok(new
        {
            scannedStocks = stocks.Count,
            totalFlatRuns = ordered.Count,
            nearCorporateActionCount = ordered.Count(r => (bool)((dynamic)r).nearCorporateAction),
            runs = ordered
        });
    }

    /// <summary>
    /// DB'YE YAZAR — tek bir hissenin kurumsal olaylarını yeniden çeker: mevcut kayıtlarını siler,
    /// KAP (skipKap=false ise) + İş Yatırım'ı birleştirip yeniden ekler. Toplu /corporate-actions/sync
    /// alfabetik sırayla TÜM hisseleri işliyor — proje sohbeti: BIST 100 gibi öncelikli bir alt kümeyi
    /// sıraya girmeden hemen doldurmak için bu endpoint'ten tek tek (script ile) çağrılıyor.
    /// </summary>
    [AdminApiKey]
    [HttpPost("{symbol}/corporate-actions/sync")]
    public async Task<IActionResult> SyncSingleStockCorporateActions(
        string symbol,
        [FromQuery] bool skipKap = false,
        [FromServices] IKapCorporateActionService kap = null!,
        [FromServices] IIsYatirimCorporateActionService isYatirim = null!,
        [FromServices] IUnitOfWork uow = null!,
        CancellationToken ct = default)
    {
        symbol = symbol.Trim().ToUpperInvariant();
        var stock = await uow.Stocks.GetBySymbolAsync(symbol, ct);
        if (stock is null)
            return NotFound(new { message = $"{symbol} bulunamadı." });

        var kapActions = skipKap
            ? (IReadOnlyList<CorporateAction>)Array.Empty<CorporateAction>()
            : await kap.GetCorporateActionsAsync(symbol, sinceDate: null, ct);
        var isYatirimActions = await isYatirim.GetCorporateActionsAsync(symbol, ct);
        var merged = CorporateActionMerge.MergePreferKap(kapActions, isYatirimActions);

        var removed = await uow.CorporateActions.DeleteAllByStockIdAsync(stock.Id, ct);
        await uow.SaveChangesAsync(ct);

        foreach (var action in merged)
        {
            action.StockId = stock.Id;
            await uow.CorporateActions.AddAsync(action, ct);
        }
        await uow.SaveChangesAsync(ct);

        return Ok(new
        {
            symbol,
            removed,
            added = merged.Count,
            kapActionCount = kapActions.Count,
            isYatirimActionCount = isYatirimActions.Count
        });
    }

    /// <summary>
    /// TANI AMAÇLI, DB'ye HİÇ YAZMAZ: kap-fresh-audit'in aksine KAP/İş Yatırım'a HİÇ gitmez —
    /// sadece DB'de zaten kayıtlı (senkron edilmiş) kurumsal olayları kullanır. Arkada devam eden
    /// tam geçmiş dolgu (corporate-actions/sync?full=true&resume=true&sync=true) sürerken, henüz
    /// bitmemiş hisseler için KAP'a fazladan istek atıp yükü ikiye katlamadan, bitmiş hisseleri
    /// hızlıca kontrol etmek için kullanılıyor.
    /// </summary>
    [HttpGet("{symbol}/db-audit")]
    public async Task<IActionResult> DbAudit(
        string symbol,
        [FromServices] IUnitOfWork uow,
        [FromServices] IndependentAdjustmentAuditService independentAuditor,
        [FromServices] TvImpliedFactorAuditService impliedAuditor,
        CancellationToken ct)
    {
        symbol = symbol.Trim().ToUpperInvariant();
        var stock = await uow.Stocks.GetBySymbolAsync(symbol, ct);
        if (stock is null)
            return NotFound(new { message = $"{symbol} bulunamadı." });

        var actions = await uow.CorporateActions.GetByStockIdAsync(stock.Id, ct);
        var prices = await uow.PriceHistories.GetByStockIdAsync(stock.Id, ct: ct);

        var independentResult = independentAuditor.Audit(symbol, prices, actions);
        var impliedResult = impliedAuditor.Audit(symbol, prices, actions);

        return Ok(new
        {
            symbol,
            dbActionCount = actions.Count,
            dbActions = actions.Select(a => new
            {
                a.ActionType, a.ActionDate, a.Value, a.SubscriptionPrice, a.Description
            }),
            independent = new
            {
                independentResult.DaysCompared,
                mismatchCount = independentResult.Mismatches.Count,
                sampleMismatches = independentResult.Mismatches.Take(10).Select(m => new
                {
                    m.Date, m.Computed, m.Actual, m.RatioDiff
                })
            },
            implied = new
            {
                impliedResult.HasIssues,
                missingSteps = impliedResult.MissingSteps,
                magnitudeMismatches = impliedResult.MagnitudeMismatches,
                unexplainedSteps = impliedResult.UnexplainedSteps
            }
        });
    }

    /// <summary>
    /// TEK SEFERLİK/MANUEL: bir hissenin sembolünü (ve adını) YERİNDE değiştirir — satır ID'si aynı
    /// kaldığı için fiyat geçmişi, kurumsal olaylar ve varsa portföy/izleme listesi bağlantıları
    /// KOPMAZ. Proje sohbeti: KOZAL şirketi KAP'ta artık "TRALT" (Türk Altın İşletmeleri A.Ş.) olarak
    /// kayıtlı — bu, sil-yeniden-oluştur yerine güvenli bir yeniden adlandırma için kullanılıyor.
    /// </summary>
    [AdminApiKey]
    [HttpPost("{oldSymbol}/rename")]
    public async Task<IActionResult> RenameSymbol(
        string oldSymbol,
        [FromQuery] string newSymbol,
        [FromQuery] string? newName,
        [FromServices] IUnitOfWork uow,
        CancellationToken ct)
    {
        oldSymbol = oldSymbol.Trim().ToUpperInvariant();
        newSymbol = newSymbol.Trim().ToUpperInvariant();

        var stock = await uow.Stocks.GetBySymbolAsync(oldSymbol, ct);
        if (stock is null)
            return NotFound(new { message = $"{oldSymbol} bulunamadı." });

        var clash = await uow.Stocks.GetBySymbolAsync(newSymbol, ct);
        if (clash is not null)
            return Conflict(new { message = $"{newSymbol} zaten mevcut, yeniden adlandırma iptal edildi." });

        var oldName = stock.Name;
        stock.Symbol = newSymbol;
        stock.YahooSymbol = $"{newSymbol}.IS";
        if (!string.IsNullOrWhiteSpace(newName))
            stock.Name = newName.Trim();
        stock.UpdatedAt = DateTime.UtcNow;
        uow.Stocks.Update(stock);
        await uow.SaveChangesAsync(ct);

        return Ok(new { message = $"{oldSymbol} → {newSymbol} olarak yeniden adlandırıldı (geçmiş korundu).", oldSymbol, newSymbol, oldName, newName = stock.Name });
    }

    /// <summary>
    /// Her hisse için TradingView'in KENDİ implied factor'ündeki (AdjustedClose/Close) basamak
    /// atlamalarını bulup kayıtlı büyük kurumsal olaylarımızla (BonusIssue/RightsIssue) eşleştirir
    /// — sınır günü TAHMİN EDİLMEZ, TV'nin kendi verisinden okunur (bkz. TvImpliedFactorAuditService).
    /// </summary>
    [AdminApiKey]
    [HttpPost("tv-implied-factor-audit")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> TvImpliedFactorAudit(
        [FromServices] TvImpliedFactorAuditJob job,
        [FromQuery] string? symbol = null,
        [FromQuery] bool sync = false,
        CancellationToken ct = default)
    {
        if (sync)
        {
            await job.RunAsync(symbol, ct);
            return Ok(new { message = "TV implied factor denetimi tamamlandı (senkron) — detaylar loglarda.", symbol });
        }

        var jobId = _jobs.Enqueue<TvImpliedFactorAuditJob>(j => j.RunAsync(symbol, CancellationToken.None));

        return Accepted(new
        {
            message = "TV implied factor denetimi başladı.",
            symbol,
            jobId,
        });
    }

    [AdminApiKey]
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
