using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Seeds;
using SanalBorsa.Application.Indices.Commands.BootstrapMarketIndices;
using SanalBorsa.Application.Stocks.Commands.BootstrapMarketData;
using SanalBorsa.Application.Stocks.Commands.SyncStocks;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Infrastructure.Jobs;

/// <summary>
/// Runs at startup to bootstrap missing market data, then triggers a daily price sync
/// after BIST market close (18:35 Istanbul / 15:35 UTC) on every weekday.
/// </summary>
public class InitialDataSeedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InitialDataSeedService> _logger;

    public InitialDataSeedService(IServiceScopeFactory scopeFactory, ILogger<InitialDataSeedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give the app time to fully start before touching the DB
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        await RunStartupBootstrapAsync(stoppingToken);

        // Daily sync loop — fires after each BIST close
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = GetDelayUntilNextSync();
            _logger.LogInformation(
                "Daily price sync scheduled in {Hours:F1}h (next BIST close + 35 min)",
                delay.TotalHours);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (stoppingToken.IsCancellationRequested) break;

            await RunDailySyncAsync(stoppingToken);
        }
    }

    // ── startup ──────────────────────────────────────────────────────────────

    private async Task RunStartupBootstrapAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var uow      = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            var existingStocks = await uow.Stocks.GetAllAsync(ct);
            var hasPriceData   = await uow.PriceHistories.AnyAsync(ct);

            // ── 1. bootstrap hisseler ──────────────────────────────────────
            if (existingStocks.Count == 0 || !hasPriceData)
            {
                _logger.LogInformation(
                    "Stock bootstrap required — Stocks: {StockCount}, HasPriceData: {HasPriceData}",
                    existingStocks.Count, hasPriceData);
                await mediator.Send(new BootstrapMarketDataCommand(), ct);
            }
            else
            {
                _logger.LogInformation(
                    "Database has {StockCount} stocks and price history — skipping full bootstrap",
                    existingStocks.Count);
            }

            // ── 2. bootstrap endeksler (BIST100, XU030, USD/TRY …) ────────
            var indexSymbols = MarketInstrumentSeed.All.Select(e => e.Symbol).ToList();
            var indexStocks  = await uow.Stocks.GetBySymbolsAsync(indexSymbols, ct);
            var indicesNeedBootstrap = indexStocks.Count < indexSymbols.Count
                || indexStocks.Any(s => s.EarliestDataDate is null || s.NeedsHistoryRefresh);

            if (indicesNeedBootstrap)
            {
                _logger.LogInformation("Market instruments bootstrap required — fetching index/FX history…");
                await mediator.Send(new BootstrapMarketIndicesCommand(), ct);
            }

            // ── 3. veri eskiyse startup'ta da sync yap ────────────────────
            var regularStocks = existingStocks
                .Where(s => !MarketInstrumentSeed.IsMarketInstrument(s.Exchange))
                .ToList();

            if (regularStocks.Count > 0)
            {
                var latestDate = regularStocks
                    .Where(s => s.LatestDataDate.HasValue)
                    .Select(s => s.LatestDataDate!.Value.Date)
                    .DefaultIfEmpty(DateTime.MinValue.Date)
                    .Max();

                // Pazar günü kontrol etme; en son iş günü veri olması yeterli
                var expectedLatest = LastBusinessDay(DateTime.UtcNow.Date);

                if (latestDate < expectedLatest)
                {
                    _logger.LogInformation(
                        "Price data is stale (latest: {LatestDate:yyyy-MM-dd}, expected: {Expected:yyyy-MM-dd}) — syncing…",
                        latestDate, expectedLatest);
                    await RunDailySyncAsync(ct);
                    return; // sync zaten tüm hisseleri ve endeksleri kapsıyor
                }
            }

            _logger.LogInformation("Startup market bootstrap completed — data is up-to-date");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Startup bootstrap failed");
        }
    }

    // ── daily sync ───────────────────────────────────────────────────────────

    private async Task RunDailySyncAsync(CancellationToken ct)
    {
        _logger.LogInformation("Running daily price sync (stocks + indices)…");
        try
        {
            await using var scope    = _scopeFactory.CreateAsyncScope();
            var mediator             = scope.ServiceProvider.GetRequiredService<IMediator>();
            var result               = await mediator.Send(new SyncStocksCommand(), ct);
            _logger.LogInformation(
                "Daily sync complete — updated: {Updated}, prices added: {Prices}",
                result.StocksUpdated, result.PriceRecordsAdded);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Daily price sync failed");
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Calculates the delay until the next sync window:
    /// weekdays at 18:35 Istanbul time (= 15:35 UTC, 35 min after BIST close).
    /// </summary>
    private static TimeSpan GetDelayUntilNextSync()
    {
        // UTC+3 için sabit offset kullanıyoruz (İstanbul kış/yaz saati farkı yok)
        const int istanbulOffsetHours = 3;
        var istNow = DateTime.UtcNow.AddHours(istanbulOffsetHours);

        // Hedef: İstanbul saatiyle 18:35
        var candidate = istNow.Date.AddHours(18).AddMinutes(35);

        // Zaman geçtiyse yarına taşı
        if (istNow >= candidate)
            candidate = candidate.AddDays(1);

        // Hafta sonu atlat
        while (candidate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            candidate = candidate.AddDays(1);

        return candidate - istNow;
    }

    /// <summary>
    /// Returns the most recent business day on or before the given date.
    /// </summary>
    private static DateTime LastBusinessDay(DateTime date)
    {
        var d = date;
        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            d = d.AddDays(-1);
        return d;
    }
}
