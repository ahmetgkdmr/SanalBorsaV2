using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using SanalBorsa.Application.Stocks.Commands.SyncBistDailyPrices;
using SanalBorsa.Application.Stocks.Commands.SyncStocks;

namespace SanalBorsa.Infrastructure.Jobs;

/// <summary>
/// Her gün 19:00 Türkiye — önce metadata, sonra BIST ham günlük fiyatlar (TradingView WS).
/// Her hisse için LatestDataDate → bugün aralığını çeker ve DB'ye yazar.
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
            _logger.LogInformation(
                "Metadata sync done — updated={Updated}",
                meta.StocksUpdated);

            var result = await mediator.Send(
                new SyncBistDailyPricesCommand(),
                context.CancellationToken);

            _logger.LogInformation(
                "TradingViewPriceSyncJob done — attempted={A} synced={S} bars={B} failed={F} maxLatest={Max:yyyy-MM-dd}",
                result.Attempted,
                result.Synced,
                result.BarsUpserted,
                result.Failed,
                result.MaxLatestDate);

            if (result.Error is not null)
                throw new JobExecutionException(new InvalidOperationException(result.Error), refireImmediately: false);
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
