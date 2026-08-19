using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Indices.Commands.SyncParityHistory;
using SanalBorsa.Application.Stocks.Commands.ComputeTimeMachineLeaders;

namespace SanalBorsa.Infrastructure.Jobs;

/// <summary>
/// Her gece BIST ve kripto fiyat senkronları bittikten sonra çalışır:
/// önce USD/TRY · EUR/TRY · gram altın serilerini tazeler, ardından
/// "o gün alsaydın bugün" tablosunu baştan üretir.
/// Hangfire recurring job; kayıt: <see cref="RecurringJobRegistrar"/>.
/// </summary>
/// <remarks>
/// Tablo tamamen yeniden üretilir çünkü getiri son kapanışa göre ölçülür —
/// bugünün fiyatı değişince geçmişteki her günün sıralaması da değişir.
/// </remarks>
[DisableConcurrentExecution(timeoutInSeconds: 600)]
[AutomaticRetry(Attempts = 3)]
public sealed class TimeMachineLeadersJob
{
    private readonly IMediator _mediator;
    private readonly IBackgroundJobClient _jobs;
    private readonly ILogger<TimeMachineLeadersJob> _logger;

    public TimeMachineLeadersJob(
        IMediator mediator, IBackgroundJobClient jobs, ILogger<TimeMachineLeadersJob> logger)
    {
        _mediator = mediator;
        _jobs = jobs;
        _logger = logger;
    }

    /// <param name="isRetry">
    /// Bir önceki çalıştırmanın planladığı tek seferlik tekrar mı — sonsuz retry zincirini
    /// önlemek için true ise tekrar bir retry PLANLAMAZ, sadece bu son denemeyi yapar.
    /// </param>
    public async Task RunAsync(CancellationToken ct = default, bool isRetry = false)
    {
        _logger.LogInformation(
            "TimeMachineLeadersJob started at {Time} (retry={IsRetry})", DateTimeOffset.UtcNow, isRetry);

        try
        {
            var parity = await _mediator.Send(new SyncParityHistoryCommand(), ct);
            foreach (var detail in parity.Details)
            {
                _logger.LogInformation(
                    "Parite {Symbol}: {Rows} satır, son {Latest:yyyy-MM-dd}{Error}{Retry}",
                    detail.Symbol,
                    detail.RowsWritten,
                    detail.LatestDate,
                    detail.Error is null ? string.Empty : $" — HATA: {detail.Error}",
                    detail.NeedsRetry ? " — RETRY GEREKİYOR" : string.Empty);
            }

            if (parity.NeedsRetry && !isRetry)
            {
                _jobs.Schedule<TimeMachineLeadersJob>(j => j.RunAsync(CancellationToken.None, true), TimeSpan.FromHours(1));
                _logger.LogWarning(
                    "TimeMachineLeadersJob: en az bir parite sembolünün en yeni günü şüpheli — 1 saat sonra tek seferlik retry planlandı.");
            }

            var result = await _mediator.Send(new ComputeTimeMachineLeadersCommand(), ct);

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
            throw;
        }
    }
}
