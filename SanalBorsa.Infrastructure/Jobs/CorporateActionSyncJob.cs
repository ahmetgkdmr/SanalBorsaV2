using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common;
using SanalBorsa.Application.Stocks.Commands.SyncBistAdjustedCloses;
using SanalBorsa.Application.Stocks.Commands.SyncCorporateActions;

namespace SanalBorsa.Infrastructure.Jobs;

/// <summary>
/// Nightly 18:35 Turkey — incremental KAP check for new corporate actions
/// (bedelsiz / bedelli+rüçhan / nakit temettü) after the latest DB date.
/// Full historical bootstrap uses POST …/corporate-actions/sync?full=true (İş Yatırım).
///
/// AdjustedClose tam geçmişi ancak o hissede YENİ bir kurumsal olay eklendiğinde değişir
/// (yeni bar'lar zaten <c>AdjustedClose = Close</c> placeholder'ıyla giriyor, bkz.
/// SyncBistDailyPricesCommandHandler). Bu yüzden tam yenileme artık tüm hisselerde değil,
/// sadece bu koşuda gerçekten yeni olay eklenen sembollerde tetikleniyor — 645 hisse yerine
/// günde birkaç istek, TradingView'e gereksiz yük/rate-limit riskini önlüyor.
/// Hangfire recurring job; kayıt: <see cref="RecurringJobRegistrar"/>.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 600)]
[AutomaticRetry(Attempts = 3)]
public sealed class CorporateActionSyncJob
{
    private readonly IMediator _mediator;
    private readonly ILogger<CorporateActionSyncJob> _logger;
    private readonly MarketDataCacheVersion _cacheVersion;

    public CorporateActionSyncJob(
        IMediator mediator,
        ILogger<CorporateActionSyncJob> logger,
        MarketDataCacheVersion cacheVersion)
    {
        _mediator = mediator;
        _logger = logger;
        _cacheVersion = cacheVersion;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("CorporateActionSyncJob (KAP) started at {Time}", DateTimeOffset.UtcNow);

        try
        {
            var result = await _mediator.Send(new SyncCorporateActionsCommand(FullResync: false), ct);

            _logger.LogInformation(
                "CorporateActionSyncJob (KAP) completed — Processed: {Processed}, Skipped: {Skipped}, Added: {Added}, Failed: {Failed}, Affected: {Affected}",
                result.StocksProcessed, result.StocksSkipped, result.ActionsAdded, result.Failed, result.AffectedSymbols.Count);

            await RefreshAdjustedClosesAsync(result.AffectedSymbols, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CorporateActionSyncJob (KAP) failed");
            throw;
        }
    }

    private async Task RefreshAdjustedClosesAsync(IReadOnlyList<string> symbols, CancellationToken ct)
    {
        if (symbols.Count == 0)
        {
            _logger.LogInformation("CorporateActionSyncJob — bugün yeni kurumsal olay yok, AdjustedClose yenilemesi atlandı");
            return;
        }

        _logger.LogInformation(
            "CorporateActionSyncJob — {Count} hissede yeni olay bulundu, AdjustedClose yenileniyor: {Symbols}",
            symbols.Count, string.Join(", ", symbols));

        _cacheVersion.BumpBist();

        foreach (var symbol in symbols)
        {
            try
            {
                var adj = await _mediator.Send(new SyncBistAdjustedClosesCommand(Symbol: symbol), ct);
                _logger.LogInformation(
                    "CorporateActionSyncJob — {Symbol} AdjustedClose yenilendi — rows={Rows} failed={Failed}",
                    symbol, adj.RowsUpdated, adj.Failed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CorporateActionSyncJob — {Symbol} AdjustedClose yenileme hatası", symbol);
            }
        }
    }
}
