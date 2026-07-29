using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Seeds;
using SanalBorsa.Application.Indices.Commands.BootstrapMarketIndices;
using SanalBorsa.Application.Stocks.Commands.BootstrapMarketData;
using SanalBorsa.Application.Stocks.Commands.SyncBistDailyPrices;
using SanalBorsa.Application.Stocks.Commands.SyncStocks;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Infrastructure.Jobs;

/// <summary>
/// Startup bootstrap: eksik sembol/endeks verisini doldurur.
/// Veri stale ise bir kez BIST ham sync çalıştırır.
/// Günlük planlı sync Quartz <see cref="TradingViewPriceSyncJob"/> (18:30 TR) üzerinden yapılır.
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
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        await RunStartupBootstrapAsync(stoppingToken);
    }

    private async Task RunStartupBootstrapAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            var existingStocks = await uow.Stocks.GetAllAsync(ct);
            var hasPriceData = await uow.PriceHistories.AnyAsync(ct);

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

            var indexSymbols = MarketInstrumentSeed.All.Select(e => e.Symbol).ToList();
            var indexStocks = await uow.Stocks.GetBySymbolsAsync(indexSymbols, ct);
            var indicesNeedBootstrap = indexStocks.Count < indexSymbols.Count
                || indexStocks.Any(s => s.EarliestDataDate is null || s.NeedsHistoryRefresh);

            if (indicesNeedBootstrap)
            {
                _logger.LogInformation("Market instruments bootstrap required — fetching index/FX history…");
                await mediator.Send(new BootstrapMarketIndicesCommand(), ct);
            }

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

                var expectedLatest = LastBusinessDay(DateTime.UtcNow.Date);

                if (latestDate < expectedLatest)
                {
                    _logger.LogInformation(
                        "Price data is stale (latest: {LatestDate:yyyy-MM-dd}, expected: {Expected:yyyy-MM-dd}) — syncing…",
                        latestDate, expectedLatest);
                    await RunCatchUpSyncAsync(mediator, ct);
                }
            }

            _logger.LogInformation("Startup market bootstrap completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Startup bootstrap failed");
        }
    }

    private async Task RunCatchUpSyncAsync(IMediator mediator, CancellationToken ct)
    {
        _logger.LogInformation("Running startup catch-up sync (metadata + BIST ham)…");
        try
        {
            var meta = await mediator.Send(new SyncStocksCommand(), ct);
            _logger.LogInformation(
                "Startup metadata sync — updated: {Updated}",
                meta.StocksUpdated);

            var bist = await mediator.Send(new SyncBistDailyPricesCommand(), ct);
            _logger.LogInformation(
                "Startup BIST ham sync — synced={S} bars={B} failed={F} max={Max:yyyy-MM-dd}",
                bist.Synced, bist.BarsUpserted, bist.Failed, bist.MaxLatestDate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Startup catch-up sync failed");
        }
    }

    private static DateTime LastBusinessDay(DateTime date)
    {
        var d = date;
        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            d = d.AddDays(-1);
        return d;
    }
}
