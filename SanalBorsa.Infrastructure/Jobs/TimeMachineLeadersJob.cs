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
    private readonly ILogger<TimeMachineLeadersJob> _logger;

    public TimeMachineLeadersJob(IMediator mediator, ILogger<TimeMachineLeadersJob> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("TimeMachineLeadersJob started at {Time}", DateTimeOffset.UtcNow);

        try
        {
            var parity = await _mediator.Send(new SyncParityHistoryCommand(), ct);
            foreach (var detail in parity.Details)
            {
                _logger.LogInformation(
                    "Parite {Symbol}: {Rows} satır, son {Latest:yyyy-MM-dd}{Error}",
                    detail.Symbol,
                    detail.RowsWritten,
                    detail.LatestDate,
                    detail.Error is null ? string.Empty : $" — HATA: {detail.Error}");
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
