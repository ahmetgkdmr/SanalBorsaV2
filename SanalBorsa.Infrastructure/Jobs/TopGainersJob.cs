using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Stocks.Commands.ComputeTopGainers;
using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Infrastructure.Jobs;

/// <summary>
/// Her gece 23:00 Türkiye saati — BIST ve Crypto için 5 dönem şampiyonunu
/// (1h / 1a / 1y / 5y / 10y) DB'deki son kapanışa göre yeniden hesaplar.
/// Hangfire recurring job; kayıt: <see cref="RecurringJobRegistrar"/>.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 600)]
[AutomaticRetry(Attempts = 3)]
public sealed class TopGainersJob
{
    private readonly IMediator _mediator;
    private readonly ILogger<TopGainersJob> _logger;

    public TopGainersJob(IMediator mediator, ILogger<TopGainersJob> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("TopGainersJob started at {Time}", DateTimeOffset.UtcNow);

        try
        {
            foreach (var market in new[] { MarketType.Bist, MarketType.Crypto })
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
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TopGainersJob failed");
            throw;
        }
    }
}
