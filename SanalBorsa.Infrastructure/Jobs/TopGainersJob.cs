using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common;
using SanalBorsa.Application.Stocks.Commands.ComputeTopGainers;
using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Infrastructure.Jobs;

/// <summary>
/// BIST/Crypto için her gece 23:00 Türkiye saati, ABD hisseleri için NYSE kapanışından sonra
/// 16:30 ET (ayrı bir cron kaydıyla, bkz. <see cref="RecurringJobRegistrar"/>) — verilen piyasalar
/// için 5 dönem şampiyonunu (1h / 1a / 1y / 5y / 10y) DB'deki son kapanışa göre yeniden hesaplar.
/// ABD'nin ayrı saatte olmasının nedeni: 23:00 TR, ABD'nin kendi günlük fiyat senkronundan
/// (16:10–16:20 ET ≈ 23:10–00:20 TR, mevsime göre değişir) önce gelebiliyor — aynı job'a
/// eklenirse bazı aylarda bir önceki günün verisiyle hesaplanmış olurdu.
/// Hangfire recurring job; kayıt: <see cref="RecurringJobRegistrar"/>.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 600)]
[AutomaticRetry(Attempts = 3)]
public sealed class TopGainersJob
{
    private readonly IMediator _mediator;
    private readonly ILogger<TopGainersJob> _logger;
    private readonly MarketDataCacheVersion _cacheVersion;

    public TopGainersJob(
        IMediator mediator,
        ILogger<TopGainersJob> logger,
        MarketDataCacheVersion cacheVersion)
    {
        _mediator = mediator;
        _logger = logger;
        _cacheVersion = cacheVersion;
    }

    public async Task RunAsync(IReadOnlyList<MarketType> markets, CancellationToken ct = default)
    {
        _logger.LogInformation("TopGainersJob started at {Time} for {Markets}", DateTimeOffset.UtcNow, string.Join(",", markets));

        try
        {
            foreach (var market in markets)
            {
                var result = await _mediator.Send(new ComputeTopGainersCommand(market), ct);
                _logger.LogInformation(
                    "TopGainersJob {Market} — AsOf={AsOf:yyyy-MM-dd} Week={Week} Month={Month} Year={Year} FiveY={FiveY} TenY={TenY}",
                    result.MarketType,
                    result.AsOfDate,
                    result.WeekChampion,
                    result.MonthChampion,
                    result.YearChampion,
                    result.FiveYearChampion,
                    result.TenYearChampion);

                if (market == MarketType.UsStocks) _cacheVersion.BumpUs();
                else if (market == MarketType.Bist) _cacheVersion.BumpBist();
                else if (market == MarketType.Crypto) _cacheVersion.BumpCrypto();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TopGainersJob failed");
            throw;
        }
    }
}
