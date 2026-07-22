using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using SanalBorsa.Application.Crypto.Commands.SyncCryptoHistory;

namespace SanalBorsa.Infrastructure.Jobs;

/// <summary>
/// Binance USDT günlük kline incremental sync (UTC 01:30 ≈ TR 04:30).
/// </summary>
[DisallowConcurrentExecution]
public sealed class CryptoHistorySyncJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CryptoHistorySyncJob> _logger;

    public CryptoHistorySyncJob(IServiceScopeFactory scopeFactory, ILogger<CryptoHistorySyncJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("CryptoHistorySyncJob started");
        await using var scope = _scopeFactory.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        try
        {
            var result = await mediator.Send(new SyncCryptoHistoryCommand(), context.CancellationToken);
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
            throw new JobExecutionException(ex, refireImmediately: false);
        }
    }
}
