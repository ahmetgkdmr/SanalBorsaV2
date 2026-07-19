using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using SanalBorsa.Application.Stocks.Commands.SyncCorporateActions;

namespace SanalBorsa.Infrastructure.Jobs;

/// <summary>
/// Nightly 23:00 Turkey — incremental KAP check for new corporate actions
/// (bedelsiz / bedelli+rüçhan / nakit temettü) after the latest DB date.
/// Full historical bootstrap uses POST …/corporate-actions/sync?full=true (İş Yatırım).
/// </summary>
[DisallowConcurrentExecution]
public class CorporateActionSyncJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CorporateActionSyncJob> _logger;

    public CorporateActionSyncJob(
        IServiceScopeFactory scopeFactory,
        ILogger<CorporateActionSyncJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("CorporateActionSyncJob (KAP) started at {Time}", DateTimeOffset.UtcNow);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        try
        {
            var result = await mediator.Send(
                new SyncCorporateActionsCommand(FullResync: false),
                context.CancellationToken);

            _logger.LogInformation(
                "CorporateActionSyncJob (KAP) completed — Processed: {Processed}, Skipped: {Skipped}, Added: {Added}, Failed: {Failed}",
                result.StocksProcessed, result.StocksSkipped, result.ActionsAdded, result.Failed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CorporateActionSyncJob (KAP) failed");
            throw new JobExecutionException(ex, refireImmediately: false);
        }
    }
}
