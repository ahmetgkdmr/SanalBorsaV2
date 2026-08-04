using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Stocks.Commands.SyncCorporateActions;

namespace SanalBorsa.Infrastructure.Jobs;

/// <summary>
/// Nightly 18:35 Turkey — incremental KAP check for new corporate actions
/// (bedelsiz / bedelli+rüçhan / nakit temettü) after the latest DB date.
/// Full historical bootstrap uses POST …/corporate-actions/sync?full=true (İş Yatırım).
/// Hangfire recurring job; kayıt: <see cref="RecurringJobRegistrar"/>.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 600)]
[AutomaticRetry(Attempts = 3)]
public sealed class CorporateActionSyncJob
{
    private readonly IMediator _mediator;
    private readonly ILogger<CorporateActionSyncJob> _logger;

    public CorporateActionSyncJob(IMediator mediator, ILogger<CorporateActionSyncJob> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("CorporateActionSyncJob (KAP) started at {Time}", DateTimeOffset.UtcNow);

        try
        {
            var result = await _mediator.Send(new SyncCorporateActionsCommand(FullResync: false), ct);

            _logger.LogInformation(
                "CorporateActionSyncJob (KAP) completed — Processed: {Processed}, Skipped: {Skipped}, Added: {Added}, Failed: {Failed}",
                result.StocksProcessed, result.StocksSkipped, result.ActionsAdded, result.Failed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CorporateActionSyncJob (KAP) failed");
            throw;
        }
    }
}
