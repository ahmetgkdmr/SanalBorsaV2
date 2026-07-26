using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using SanalBorsa.Application.Stocks.Commands.ComputeTopGainers;
using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Infrastructure.Jobs;

/// <summary>
/// Her gece 23:05 Türkiye saati — BIST ve Crypto için 5 dönem şampiyonunu
/// (1h / 1a / 1y / 5y / 10y) DB'deki son kapanışa göre yeniden hesaplar.
/// </summary>
[DisallowConcurrentExecution]
public class TopGainersJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TopGainersJob> _logger;

    public TopGainersJob(IServiceScopeFactory scopeFactory, ILogger<TopGainersJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("TopGainersJob started at {Time}", DateTimeOffset.UtcNow);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        try
        {
            foreach (var market in new[] { MarketType.Bist, MarketType.Crypto })
            {
                var result = await mediator.Send(
                    new ComputeTopGainersCommand(market),
                    context.CancellationToken);
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
            throw new JobExecutionException(ex, refireImmediately: false);
        }
    }
}
