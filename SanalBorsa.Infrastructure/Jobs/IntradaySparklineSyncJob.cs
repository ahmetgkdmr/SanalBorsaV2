using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Stocks.Commands.RefreshIntradaySparkline;
using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Infrastructure.Jobs;

/// <summary>
/// Seans kapanışından hemen sonra çalışır (BIST 18:45 TR, ABD 16:05 ET) — önceki tam seans
/// gününün 15dk bar'larını çekip market'in intraday sparkline tablosunu yeniler.
/// Kayıt: <see cref="RecurringJobRegistrar"/>.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 600)]
[AutomaticRetry(Attempts = 3)]
public sealed class IntradaySparklineSyncJob
{
    private readonly IMediator _mediator;
    private readonly ILogger<IntradaySparklineSyncJob> _logger;

    public IntradaySparklineSyncJob(IMediator mediator, ILogger<IntradaySparklineSyncJob> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task RunAsync(MarketType market, CancellationToken ct = default)
    {
        _logger.LogInformation("IntradaySparklineSyncJob started for {Market} at {Time}", market, DateTimeOffset.UtcNow);

        var result = await _mediator.Send(new RefreshIntradaySparklineCommand(market), ct);

        _logger.LogInformation(
            "IntradaySparklineSyncJob ({Market}) done — attempted={A} synced={S} failed={F} bars={B}",
            market, result.Attempted, result.Synced, result.Failed, result.BarsWritten);
    }
}
