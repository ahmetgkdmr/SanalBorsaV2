using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using SanalBorsa.Application.Stocks.Commands.RefreshStockHistory;

namespace SanalBorsa.Infrastructure.Jobs;

/// <summary>
/// Runs every weekday at 20:00 (UTC+3) — one hour after DailyPriceUpdateJob.
/// Re-fetches complete history for all stocks that were flagged (NeedsHistoryRefresh = true)
/// by the DailyPriceUpdateJob because a new corporate action was detected.
/// </summary>
[DisallowConcurrentExecution]
public class HistoryRefreshJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HistoryRefreshJob> _logger;

    public HistoryRefreshJob(IServiceScopeFactory scopeFactory, ILogger<HistoryRefreshJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("HistoryRefreshJob started at {Time}", DateTimeOffset.UtcNow);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        try
        {
            var result = await mediator.Send(
                new RefreshStockHistoryCommand(),
                context.CancellationToken);

            _logger.LogInformation(
                "HistoryRefreshJob completed — Stocks refreshed: {Stocks}, Records inserted: {Records}",
                result.StocksRefreshed, result.PriceRecordsInserted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HistoryRefreshJob failed");
            throw new JobExecutionException(ex, refireImmediately: false);
        }
    }
}
