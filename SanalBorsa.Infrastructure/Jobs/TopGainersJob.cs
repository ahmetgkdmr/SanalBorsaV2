using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using SanalBorsa.Application.Stocks.Commands.ComputeTopGainers;

namespace SanalBorsa.Infrastructure.Jobs;

/// <summary>
/// Nightly 23:05 Turkey — recomputes week / month / year top gainer champions
/// using the latest close in DB (typically last Friday when run on weekend).
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
            var result = await mediator.Send(new ComputeTopGainersCommand(), context.CancellationToken);
            _logger.LogInformation(
                "TopGainersJob completed — AsOf={AsOf:yyyy-MM-dd} Week={Week} Month={Month} Year={Year}",
                result.AsOfDate, result.WeekChampion, result.MonthChampion, result.YearChampion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TopGainersJob failed");
            throw new JobExecutionException(ex, refireImmediately: false);
        }
    }
}
