using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using SanalBorsa.Application.Indices.Commands.SyncParityHistory;
using SanalBorsa.Application.Stocks.Commands.ComputeTimeMachineLeaders;

namespace SanalBorsa.Infrastructure.Jobs;

/// <summary>
/// Her gece BIST ve kripto fiyat senkronları bittikten sonra çalışır:
/// önce USD/TRY · EUR/TRY · gram altın serilerini tazeler, ardından
/// "o gün alsaydın bugün" tablosunu baştan üretir.
/// </summary>
/// <remarks>
/// Tablo tamamen yeniden üretilir çünkü getiri son kapanışa göre ölçülür —
/// bugünün fiyatı değişince geçmişteki her günün sıralaması da değişir.
/// </remarks>
[DisallowConcurrentExecution]
public class TimeMachineLeadersJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TimeMachineLeadersJob> _logger;

    public TimeMachineLeadersJob(IServiceScopeFactory scopeFactory, ILogger<TimeMachineLeadersJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("TimeMachineLeadersJob started at {Time}", DateTimeOffset.UtcNow);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        try
        {
            var parity = await mediator.Send(new SyncParityHistoryCommand(), context.CancellationToken);
            foreach (var detail in parity.Details)
            {
                _logger.LogInformation(
                    "Parite {Symbol}: {Rows} satır, son {Latest:yyyy-MM-dd}{Error}",
                    detail.Symbol,
                    detail.RowsWritten,
                    detail.LatestDate,
                    detail.Error is null ? string.Empty : $" — HATA: {detail.Error}");
            }

            var result = await mediator.Send(
                new ComputeTimeMachineLeadersCommand(), context.CancellationToken);

            foreach (var category in result.Categories)
            {
                _logger.LogInformation(
                    "TimeMachineLeaders {Category}: {Days} gün / {Rows} satır ({Earliest:yyyy-MM-dd} → {End:yyyy-MM-dd}) {Elapsed} ms{Error}",
                    category.Category,
                    category.Days,
                    category.Rows,
                    category.EarliestStartDate,
                    category.EndDate,
                    category.ElapsedMs,
                    category.Error is null ? string.Empty : $" — HATA: {category.Error}");
            }

            _logger.LogInformation("TimeMachineLeadersJob finished in {Elapsed} ms", result.ElapsedMs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TimeMachineLeadersJob failed");
            throw new JobExecutionException(ex, refireImmediately: false);
        }
    }
}
