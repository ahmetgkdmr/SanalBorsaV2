using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common;
using SanalBorsa.Application.Stocks.Commands.SyncBistAdjustedCloses;
using SanalBorsa.Application.Stocks.Commands.SyncUsAdjustedCloses;
using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Infrastructure.Jobs;

/// <summary>
/// Bir hissenin düzeltilmiş (AdjustedClose) fiyatı taze TradingView verisiyle ham fiyatla
/// çelişiyorsa (bkz. AdjustedCloseSanityCheck — Sync*AdjustedClosesCommandHandler yazmadan önce
/// kontrol eder ve hisseyi alım/satıma kapatır), bu job <see cref="AnomalyRetryPolicy"/>
/// zamanlamasıyla (önce 5dk×50, sonra 15dk×20) kendini yeniden zamanlayarak kaynağı tekrar dener:
/// - düzeldiyse (handler zaten alım/satımı tekrar açmıştır): döngü biter.
/// - politika tükendiyse (~9 saat sonra): pes edilir, kapalı durum bir sonraki BAŞARILI senkrona
///   kadar kalıcı kalır.
/// </summary>
[AutomaticRetry(Attempts = 2)]
public sealed class AdjustedCloseRetryJob
{
    private readonly IMediator _mediator;
    private readonly IBackgroundJobClient _jobs;
    private readonly ILogger<AdjustedCloseRetryJob> _logger;

    public AdjustedCloseRetryJob(IMediator mediator, IBackgroundJobClient jobs, ILogger<AdjustedCloseRetryJob> logger)
    {
        _mediator = mediator;
        _jobs = jobs;
        _logger = logger;
    }

    public async Task RetryAsync(MarketType market, string symbol, int attempt, CancellationToken ct = default)
    {
        var suspicious = market == MarketType.Bist
            ? (await _mediator.Send(new SyncBistAdjustedClosesCommand(false, symbol, null, attempt), ct)).Suspicious
            : (await _mediator.Send(new SyncUsAdjustedClosesCommand(symbol, null, attempt), ct)).Suspicious;

        if (suspicious == 0)
        {
            _logger.LogInformation(
                "AdjustedCloseRetryJob: {Symbol} deneme {Attempt} sonrası düzeldi, alım/satım tekrar açıldı.",
                symbol, attempt);
            return;
        }

        if (AnomalyRetryPolicy.ShouldGiveUp(attempt))
        {
            _logger.LogWarning(
                "AdjustedCloseRetryJob: {Symbol} {MaxAttempts} denemeden sonra hâlâ düzelmedi — pes edildi. " +
                "Alım/satım bir sonraki başarılı senkrona kadar kapalı kalacak.",
                symbol, AnomalyRetryPolicy.MaxAttempts);
            return;
        }

        _jobs.Schedule<AdjustedCloseRetryJob>(
            j => j.RetryAsync(market, symbol, attempt + 1, CancellationToken.None),
            AnomalyRetryPolicy.NextDelay(attempt));
    }
}
