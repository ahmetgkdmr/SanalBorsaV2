using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using SanalBorsa.Application.Stocks.Commands.SyncStocks;

namespace SanalBorsa.Infrastructure.Jobs;

/// <summary>
/// Runs every weekday at 19:00 (UTC+3) — after BIST market closes at 18:30.
/// Fetches the latest daily prices and checks for new corporate actions.
/// If new actions are detected, marks affected stocks for full history refresh.
/// </summary>
[DisallowConcurrentExecution]
public class DailyPriceUpdateJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DailyPriceUpdateJob> _logger;

    public DailyPriceUpdateJob(IServiceScopeFactory scopeFactory, ILogger<DailyPriceUpdateJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("DailyPriceUpdateJob started at {Time}", DateTimeOffset.UtcNow);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        try
        {
            var result = await mediator.Send(new SyncStocksCommand(), context.CancellationToken);

            _logger.LogInformation(
                "DailyPriceUpdateJob completed — Updated: {Updated}, PriceRecords: {Prices}, Actions: {Actions}",
                result.StocksUpdated, result.PriceRecordsAdded, result.ActionsAdded);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DailyPriceUpdateJob failed");
            throw new JobExecutionException(ex, refireImmediately: false);
        }
    }
}
