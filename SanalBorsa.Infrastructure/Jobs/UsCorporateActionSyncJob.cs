using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Stocks.Commands.SyncUsAdjustedCloses;
using SanalBorsa.Application.Stocks.Commands.SyncUsCorporateActions;

namespace SanalBorsa.Infrastructure.Jobs;

/// <summary>
/// Her gün 16:20 ET — ABD hisseleri için temettü/split senkronu (Yahoo Finance), ardından
/// sadece bugün gerçekten yeni olay eklenen semboller için AdjustedClose'un tam geçmişini
/// yeniden çeker. BIST'teki <see cref="CorporateActionSyncJob"/> ile birebir aynı desen:
/// yeni günlük bar'lar zaten <c>AdjustedClose = Close</c> placeholder'ıyla giriyor
/// (bkz. SyncUsDailyPricesCommandHandler), bu yüzden hiç olay yaşamamış hisselerde tam
/// yenilemeye gerek yok — 500+ hisseye günde bir istek yerine sadece etkilenenlere gidilir.
/// Hangfire recurring job; kayıt: <see cref="RecurringJobRegistrar"/>.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 600)]
[AutomaticRetry(Attempts = 3)]
public sealed class UsCorporateActionSyncJob
{
    private readonly IMediator _mediator;
    private readonly ILogger<UsCorporateActionSyncJob> _logger;

    public UsCorporateActionSyncJob(IMediator mediator, ILogger<UsCorporateActionSyncJob> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("UsCorporateActionSyncJob started at {Time}", DateTimeOffset.UtcNow);

        try
        {
            var result = await _mediator.Send(new SyncUsCorporateActionsCommand(), ct);

            _logger.LogInformation(
                "UsCorporateActionSyncJob completed — Processed: {Processed}, Skipped: {Skipped}, Added: {Added}, Failed: {Failed}, Affected: {Affected}",
                result.StocksProcessed, result.StocksSkipped, result.ActionsAdded, result.Failed, result.AffectedSymbols.Count);

            await RefreshAdjustedClosesAsync(result.AffectedSymbols, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UsCorporateActionSyncJob failed");
            throw;
        }
    }

    private async Task RefreshAdjustedClosesAsync(IReadOnlyList<string> symbols, CancellationToken ct)
    {
        if (symbols.Count == 0)
        {
            _logger.LogInformation("UsCorporateActionSyncJob — bugün yeni kurumsal olay yok, AdjustedClose yenilemesi atlandı");
            return;
        }

        _logger.LogInformation(
            "UsCorporateActionSyncJob — {Count} hissede yeni olay bulundu, AdjustedClose yenileniyor: {Symbols}",
            symbols.Count, string.Join(", ", symbols));

        foreach (var symbol in symbols)
        {
            try
            {
                var adj = await _mediator.Send(new SyncUsAdjustedClosesCommand(symbol), ct);
                _logger.LogInformation(
                    "UsCorporateActionSyncJob — {Symbol} AdjustedClose yenilendi — rows={Rows} failed={Failed}",
                    symbol, adj.RowsUpdated, adj.Failed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UsCorporateActionSyncJob — {Symbol} AdjustedClose yenileme hatası", symbol);
            }
        }
    }
}
