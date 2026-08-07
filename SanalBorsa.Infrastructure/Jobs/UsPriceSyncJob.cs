using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Stocks.Commands.SyncUsDailyPrices;

namespace SanalBorsa.Infrastructure.Jobs;

/// <summary>
/// Her gün NYSE kapanışından sonra (16:10 ET) — ABD hisseleri için ham günlük OHLC senkronu.
/// AdjustedClose burada yenilenmez — <see cref="UsCorporateActionSyncJob"/> (16:20 ET) tarafından,
/// sadece o gün kurumsal olay eklenen hisseler için tetikleniyor (bkz. o dosyadaki not — BIST'teki
/// <see cref="CorporateActionSyncJob"/> ile birebir aynı desen).
/// Hangfire recurring job; kayıt: <see cref="RecurringJobRegistrar"/>.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 600)]
[AutomaticRetry(Attempts = 3)]
public sealed class UsPriceSyncJob
{
    private readonly IMediator _mediator;
    private readonly ILogger<UsPriceSyncJob> _logger;

    public UsPriceSyncJob(IMediator mediator, ILogger<UsPriceSyncJob> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("UsPriceSyncJob started at {Time}", DateTimeOffset.UtcNow);

        try
        {
            var raw = await _mediator.Send(new SyncUsDailyPricesCommand(), ct);
            _logger.LogInformation(
                "ABD ham fiyat sync — attempted={A} synced={S} bars={B} failed={F} maxLatest={Max:yyyy-MM-dd}",
                raw.Attempted, raw.Synced, raw.BarsUpserted, raw.Failed, raw.MaxLatestDate);
            if (raw.Error is not null)
                throw new InvalidOperationException(raw.Error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UsPriceSyncJob failed");
            throw;
        }
    }
}
