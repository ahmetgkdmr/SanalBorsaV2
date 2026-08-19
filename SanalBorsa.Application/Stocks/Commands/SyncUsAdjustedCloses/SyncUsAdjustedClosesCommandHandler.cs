using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Application.Common.Services;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Stocks.Commands.SyncUsAdjustedCloses;

public class SyncUsAdjustedClosesCommandHandler
    : IRequestHandler<SyncUsAdjustedClosesCommand, SyncUsAdjustedClosesResult>
{
    private const int DelayMs = 250;

    private readonly IUnitOfWork _uow;
    private readonly ITradingViewHistoryService _tv;
    private readonly ILogger<SyncUsAdjustedClosesCommandHandler> _logger;
    private readonly MarketDataCacheVersion _cacheVersion;

    public SyncUsAdjustedClosesCommandHandler(
        IUnitOfWork uow,
        ITradingViewHistoryService tv,
        ILogger<SyncUsAdjustedClosesCommandHandler> logger,
        MarketDataCacheVersion cacheVersion)
    {
        _uow = uow;
        _tv = tv;
        _logger = logger;
        _cacheVersion = cacheVersion;
    }

    public async Task<SyncUsAdjustedClosesResult> Handle(
        SyncUsAdjustedClosesCommand request,
        CancellationToken cancellationToken)
    {
        List<Stock> stocks;

        if (!string.IsNullOrWhiteSpace(request.Symbol))
        {
            var one = await _uow.Stocks.GetBySymbolAsync(
                request.Symbol.Trim().ToUpperInvariant(), cancellationToken, MarketType.UsStocks);
            stocks = one is null ? [] : [one];
        }
        else
        {
            stocks = (await _uow.Stocks.GetAllActiveAsync(cancellationToken, MarketType.UsStocks))
                .Where(s => s.MarketType == MarketType.UsStocks)
                .Where(s => s.EarliestDataDate is not null)
                .OrderBy(s => s.Symbol)
                .ToList();
        }

        if (stocks.Count == 0)
            return new SyncUsAdjustedClosesResult(0, 0, 0, 0, "Güncellenecek ABD hissesi yok.");

        var to = DateTime.UtcNow.Date;
        var synced = 0;
        var rowsUpdated = 0;
        var failed = 0;
        var done = 0;
        var suspiciousSymbols = new List<string>();

        foreach (var stock in stocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            done++;

            try
            {
                var from = request.LookbackDays is > 0
                    ? to.AddDays(-request.LookbackDays.Value)
                    : stock.EarliestDataDate?.Date ?? DateTime.UnixEpoch;

                if (from > to)
                {
                    synced++;
                    continue;
                }

                var tvSymbol = UsExchangeResolver.ToTvSymbol(stock.Exchange, stock.Symbol);
                var adj = await _tv.GetAdjustedClosesByTvSymbolAsync(tvSymbol, from, to, cancellationToken);
                if (adj.Count == 0)
                {
                    failed++;
                    _logger.LogWarning("ABD AdjustedClose sync: {Symbol} ({TvSymbol}) için TV boş", stock.Symbol, tvSymbol);
                    continue;
                }

                // TV bazen (ROK/HUBB'da gözlemlendi — bkz. proje sohbeti) geçici olarak bozuk bir
                // seri döndürüyor: ham fiyat büyük artmışken düzeltilmiş seri büyük düşüş gösteriyor.
                // Böyle bir seriyi hiç yazma — kaynağa birazdan tekrar sorulduğunda genelde kendini
                // düzeltiyor (ROK/HUBB'ta doğrulandı); çağıran (job) bunu görüp 1 saat sonra bu tek
                // sembol için tekrar dener.
                var existingRaw = await _uow.PriceHistories.GetByStockIdAsync(stock.Id, from, to, cancellationToken);
                var actions = await _uow.CorporateActions.GetByStockIdAsync(stock.Id, cancellationToken);
                if (!AdjustedCloseSanityCheck.IsPlausible(existingRaw, adj, actions))
                {
                    suspiciousSymbols.Add(stock.Symbol);
                    _logger.LogWarning(
                        "ABD AdjustedClose sync (deneme {Attempt}): {Symbol} taze TV verisi ham fiyatla çelişiyor — yazılmadı.",
                        request.RetryAttempt, stock.Symbol);
                    await SetTradingHaltAsync(stock, cancellationToken);
                    continue;
                }

                var updated = await _uow.PriceHistories.UpdateAdjustedClosesAsync(
                    stock.Id, adj, cancellationToken);
                _uow.ClearChanges();
                await ClearTradingHaltAsync(stock, cancellationToken);

                synced++;
                rowsUpdated += updated;

                _logger.LogInformation(
                    "ABD AdjustedClose progress: {Done}/{Total} — {Symbol} (+{Rows})",
                    done, stocks.Count, stock.Symbol, updated);
            }
            catch (Exception ex)
            {
                failed++;
                _uow.ClearChanges();
                _logger.LogError(ex, "ABD AdjustedClose sync failed for {Symbol}", stock.Symbol);
            }

            if (DelayMs > 0 && done < stocks.Count)
                await Task.Delay(DelayMs, cancellationToken);
        }

        _logger.LogInformation(
            "ABD AdjustedClose sync done — attempted={A} synced={S} rows={R} failed={F} suspicious={Sus}",
            stocks.Count, synced, rowsUpdated, failed, suspiciousSymbols.Count);

        if (rowsUpdated > 0) _cacheVersion.BumpUs();

        return new SyncUsAdjustedClosesResult(
            stocks.Count, synced, rowsUpdated, failed, null, suspiciousSymbols.Count, suspiciousSymbols);
    }

    private async Task SetTradingHaltAsync(Stock stock, CancellationToken ct)
    {
        if (stock.TradingHaltReason == TradingHaltReasons.PriceInconsistency)
            return;

        stock.TradingHaltReason = TradingHaltReasons.PriceInconsistency;
        _uow.Stocks.Update(stock);
        await _uow.SaveChangesAsync(ct);
    }

    private async Task ClearTradingHaltAsync(Stock stock, CancellationToken ct)
    {
        if (stock.TradingHaltReason is null)
            return;

        stock.TradingHaltReason = null;
        _uow.Stocks.Update(stock);
        await _uow.SaveChangesAsync(ct);
    }
}
