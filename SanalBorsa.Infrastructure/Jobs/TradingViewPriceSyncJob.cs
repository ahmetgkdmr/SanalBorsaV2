using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using SanalBorsa.Application.Stocks.Commands.SyncBistAdjustedCloses;
using SanalBorsa.Application.Stocks.Commands.SyncBistDailyPrices;
using SanalBorsa.Application.Stocks.Commands.SyncStocks;

namespace SanalBorsa.Infrastructure.Jobs;

/// <summary>
/// Her gün 18:30 Türkiye — metadata + BIST ham Close (TV) + AdjustedClose (TV dividends).
/// </summary>
[DisallowConcurrentExecution]
public class TradingViewPriceSyncJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TradingViewPriceSyncJob> _logger;

    public TradingViewPriceSyncJob(
        IServiceScopeFactory scopeFactory,
        ILogger<TradingViewPriceSyncJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("TradingViewPriceSyncJob started at {Time}", DateTimeOffset.UtcNow);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        try
        {
            var meta = await mediator.Send(new SyncStocksCommand(), context.CancellationToken);
            _logger.LogInformation("Metadata sync done — updated={Updated}", meta.StocksUpdated);

            var raw = await mediator.Send(
                new SyncBistDailyPricesCommand(),
                context.CancellationToken);

            _logger.LogInformation(
                "BIST ham Close sync — attempted={A} synced={S} bars={B} failed={F} maxLatest={Max:yyyy-MM-dd}",
                raw.Attempted, raw.Synced, raw.BarsUpserted, raw.Failed, raw.MaxLatestDate);

            if (raw.Error is not null)
                throw new JobExecutionException(new InvalidOperationException(raw.Error), refireImmediately: false);

            var adj = await mediator.Send(
                new SyncBistAdjustedClosesCommand(),
                context.CancellationToken);

            _logger.LogInformation(
                "BIST AdjustedClose sync — attempted={A} synced={S} rows={R} failed={F}",
                adj.Attempted, adj.Synced, adj.RowsUpdated, adj.Failed);

            if (adj.Error is not null)
                throw new JobExecutionException(new InvalidOperationException(adj.Error), refireImmediately: false);
        }
        catch (JobExecutionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TradingViewPriceSyncJob failed");
            throw new JobExecutionException(ex, refireImmediately: false);
        }
    }
}
