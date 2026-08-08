using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Stocks.Commands.SyncBistDailyPrices;
using SanalBorsa.Application.Stocks.Commands.SyncStocks;

namespace SanalBorsa.Infrastructure.Jobs;

/// <summary>
/// Her gün 18:30 Türkiye — metadata + BIST ham Close (TV).
/// AdjustedClose burada YENİDEN hesaplanmaz — bu artık <see cref="CorporateActionSyncJob"/> (18:35)
/// tarafından, sadece o gün kurumsal olay eklenen hisseler için tetikleniyor (bkz. o dosyadaki not).
/// Hangfire recurring job; kayıt: <see cref="RecurringJobRegistrar"/>.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 600)]
[AutomaticRetry(Attempts = 3)]
public sealed class TradingViewPriceSyncJob
{
    private readonly IMediator _mediator;
    private readonly ILogger<TradingViewPriceSyncJob> _logger;

    public TradingViewPriceSyncJob(IMediator mediator, ILogger<TradingViewPriceSyncJob> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("TradingViewPriceSyncJob started at {Time}", DateTimeOffset.UtcNow);

        try
        {
            var meta = await _mediator.Send(new SyncStocksCommand(), ct);
            _logger.LogInformation("Metadata sync done — updated={Updated}", meta.StocksUpdated);

            var raw = await _mediator.Send(new SyncBistDailyPricesCommand(), ct);
            _logger.LogInformation(
                "BIST ham Close sync — attempted={A} synced={S} bars={B} failed={F} maxLatest={Max:yyyy-MM-dd}",
                raw.Attempted, raw.Synced, raw.BarsUpserted, raw.Failed, raw.MaxLatestDate);
            if (raw.Error is not null)
                throw new InvalidOperationException(raw.Error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TradingViewPriceSyncJob failed");
            throw;
        }
    }
}
