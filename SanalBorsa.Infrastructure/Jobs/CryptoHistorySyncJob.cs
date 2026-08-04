using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Crypto.Commands.SyncCryptoHistory;

namespace SanalBorsa.Infrastructure.Jobs;

/// <summary>
/// Binance USDT günlük kline incremental sync (UTC 01:30 ≈ TR 04:30).
/// Hangfire recurring job; kayıt: <see cref="RecurringJobRegistrar"/>.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 600)]
[AutomaticRetry(Attempts = 3)]
public sealed class CryptoHistorySyncJob
{
    private readonly IMediator _mediator;
    private readonly ILogger<CryptoHistorySyncJob> _logger;

    public CryptoHistorySyncJob(IMediator mediator, ILogger<CryptoHistorySyncJob> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("CryptoHistorySyncJob started");

        try
        {
            var result = await _mediator.Send(new SyncCryptoHistoryCommand(), ct);
            _logger.LogInformation(
                "CryptoHistorySyncJob done — seeded={Seeded} synced={Synced} bars={Bars} err={Err}",
                result.SymbolsSeeded,
                result.SymbolsSynced,
                result.BarsUpserted,
                result.Error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CryptoHistorySyncJob failed");
            throw;
        }
    }
}
