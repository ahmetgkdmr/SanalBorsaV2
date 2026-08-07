using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Application.Common.Seeds;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Stocks.Commands.SyncCorporateActions;

public class SyncCorporateActionsCommandHandler
    : IRequestHandler<SyncCorporateActionsCommand, SyncCorporateActionsResult>
{
    private readonly IUnitOfWork _uow;
    private readonly IKapCorporateActionService _kap;
    private readonly IIsYatirimCorporateActionService _isYatirim;
    private readonly ILogger<SyncCorporateActionsCommandHandler> _logger;

    private const int DelayIsYatirimMs = 250;
    private const int DelayKapMs = 1500;

    public SyncCorporateActionsCommandHandler(
        IUnitOfWork uow,
        IKapCorporateActionService kap,
        IIsYatirimCorporateActionService isYatirim,
        ILogger<SyncCorporateActionsCommandHandler> logger)
    {
        _uow = uow;
        _kap = kap;
        _isYatirim = isYatirim;
        _logger = logger;
    }

    public async Task<SyncCorporateActionsResult> Handle(
        SyncCorporateActionsCommand request,
        CancellationToken cancellationToken)
    {
        int processed = 0, skipped = 0, added = 0, removed = 0, failed = 0;
        var affectedSymbols = new List<string>();

        var stocks = (await _uow.Stocks.GetAllActiveAsync(cancellationToken))
            .Where(s => !MarketInstrumentSeed.IsMarketInstrument(s.Exchange))
            .OrderBy(s => s.Symbol)
            .ToList();

        // Full historical bootstrap: İş Yatırım. Nightly incremental: KAP.
        var useKap = !request.FullResync;
        var delayMs = useKap ? DelayKapMs : DelayIsYatirimMs;

        _logger.LogInformation(
            "Corporate-action sync started — {Count} stocks, FullResync={Full}, Resume={Resume}, Source={Source}",
            stocks.Count, request.FullResync, request.Resume, useKap ? "KAP" : "IsYatirim");

        if (request.FullResync && !request.Resume)
        {
            removed = await _uow.CorporateActions.DeleteAllAsync(cancellationToken);
            await _uow.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Wiped entire CorporateActions table — {Removed} rows deleted before import",
                removed);
        }

        foreach (var stock in stocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            processed++;

            try
            {
                // Resume bootstrap: stock already imported → skip (per-stock save is atomic)
                if (request.FullResync && request.Resume)
                {
                    var existing = await _uow.CorporateActions.GetLatestActionDateAsync(
                        stock.Id, cancellationToken);
                    if (existing is not null)
                    {
                        skipped++;
                        continue;
                    }
                }

                IReadOnlyList<CorporateAction> incoming;
                if (useKap)
                {
                    var latestDb = await _uow.CorporateActions.GetLatestActionDateAsync(
                        stock.Id, cancellationToken);
                    var sinceDate = latestDb?.Date.AddDays(1);

                    incoming = await _kap.GetCorporateActionsAsync(
                        stock.Symbol, sinceDate, cancellationToken);
                }
                else
                {
                    incoming = Deduplicate(await _isYatirim.GetCorporateActionsAsync(
                        stock.Symbol, cancellationToken));
                }

                if (incoming.Count == 0)
                {
                    skipped++;
                    await Task.Delay(delayMs, cancellationToken);
                    continue;
                }

                if (!request.FullResync)
                {
                    var newestApiDate = incoming.Max(a => a.ActionDate);
                    var latestDb = await _uow.CorporateActions.GetLatestActionDateAsync(
                        stock.Id, cancellationToken);

                    if (latestDb is not null && latestDb.Value.Date >= newestApiDate.Date)
                    {
                        skipped++;
                        await Task.Delay(delayMs, cancellationToken);
                        continue;
                    }
                }

                var addedForStock = 0;
                foreach (var action in incoming)
                {
                    action.StockId = stock.Id;

                    if (!request.FullResync)
                    {
                        var exists = await _uow.CorporateActions.ExistsAsync(
                            stock.Id, action.ActionDate, action.ActionType, cancellationToken);
                        if (exists)
                            continue;
                    }

                    await _uow.CorporateActions.AddAsync(action, cancellationToken);
                    added++;
                    addedForStock++;
                }

                await _uow.SaveChangesAsync(cancellationToken);
                _uow.ClearChanges();

                if (addedForStock > 0)
                    affectedSymbols.Add(stock.Symbol);

                if (processed % 25 == 0 || processed == stocks.Count)
                {
                    _logger.LogInformation(
                        "Corporate-action sync progress [{Current}/{Total}] — added={Added}, skipped={Skipped}, failed={Failed}",
                        processed, stocks.Count, added, skipped, failed);
                }

                await Task.Delay(delayMs, cancellationToken);
            }
            catch (Exception ex)
            {
                failed++;
                _uow.ClearChanges();
                _logger.LogError(ex, "Failed corporate-action sync for {Symbol}", stock.Symbol);
            }
        }

        _logger.LogInformation(
            "Corporate-action sync finished — processed={Processed}, skipped={Skipped}, added={Added}, removed={Removed}, failed={Failed}, source={Source}, affected={Affected}",
            processed, skipped, added, removed, failed, useKap ? "KAP" : "IsYatirim", affectedSymbols.Count);

        return new SyncCorporateActionsResult(processed, skipped, added, removed, failed, affectedSymbols);
    }

    private static List<CorporateAction> Deduplicate(IReadOnlyList<CorporateAction> actions)
        => actions
            .GroupBy(a => (a.ActionDate.Date, a.ActionType))
            .Select(g => g
                .OrderByDescending(a => a.Value)
                .ThenByDescending(a => a.SubscriptionPrice ?? 0m)
                .First())
            .OrderBy(a => a.ActionDate)
            .ThenBy(a => a.ActionType)
            .ToList();
}
