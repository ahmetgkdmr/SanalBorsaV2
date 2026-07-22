using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Crypto.Commands.SyncCryptoHistory;

public sealed class SyncCryptoHistoryCommandHandler
    : IRequestHandler<SyncCryptoHistoryCommand, SyncCryptoHistoryResult>
{
    private readonly IUnitOfWork _uow;
    private readonly IBinanceMarketClient _binance;
    private readonly ILogger<SyncCryptoHistoryCommandHandler> _logger;

    public SyncCryptoHistoryCommandHandler(
        IUnitOfWork uow,
        IBinanceMarketClient binance,
        ILogger<SyncCryptoHistoryCommandHandler> logger)
    {
        _uow = uow;
        _binance = binance;
        _logger = logger;
    }

    public async Task<SyncCryptoHistoryResult> Handle(
        SyncCryptoHistoryCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var filters = await _binance.GetPriceFiltersAsync(cancellationToken);
            var now = DateTime.UtcNow;
            var seeded = 0;

            IEnumerable<KeyValuePair<string, BinancePriceFilter>> toSeed = filters;
            if (!string.IsNullOrWhiteSpace(request.Symbol))
            {
                var sym = Normalize(request.Symbol);
                if (!filters.TryGetValue(sym, out var f))
                    return new SyncCryptoHistoryResult(0, 0, 0, $"{sym} Binance USDT listesinde değil.");
                toSeed = [new KeyValuePair<string, BinancePriceFilter>(sym, f)];
            }

            foreach (var (symbol, filter) in toSeed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var existing = await _uow.Stocks.GetBySymbolAsync(
                    symbol, cancellationToken, MarketType.Crypto);
                if (existing is not null)
                {
                    if (!existing.IsActive)
                    {
                        existing.IsActive = true;
                        existing.UpdatedAt = now;
                        _uow.Stocks.Update(existing);
                    }
                    continue;
                }

                await _uow.Stocks.AddAsync(new Stock
                {
                    Symbol = symbol,
                    YahooSymbol = "",
                    Name = filter.BaseAsset,
                    Currency = "USD",
                    Exchange = "BINANCE",
                    MarketType = MarketType.Crypto,
                    IsActive = true,
                    NeedsHistoryRefresh = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                }, cancellationToken);
                seeded++;
            }

            if (seeded > 0)
                await _uow.SaveChangesAsync(cancellationToken);

            var stocks = string.IsNullOrWhiteSpace(request.Symbol)
                ? await _uow.Stocks.GetAllActiveAsync(cancellationToken, MarketType.Crypto)
                : await _uow.Stocks.GetBySymbolsAsync(
                    [Normalize(request.Symbol!)], cancellationToken, MarketType.Crypto);

            // Delist: aktif listede olmayan crypto'ları pasifleştir
            if (string.IsNullOrWhiteSpace(request.Symbol))
            {
                var live = new HashSet<string>(filters.Keys, StringComparer.OrdinalIgnoreCase);
                foreach (var s in stocks.Where(s => !live.Contains(s.Symbol)))
                {
                    s.IsActive = false;
                    s.UpdatedAt = now;
                    _uow.Stocks.Update(s);
                }
                await _uow.SaveChangesAsync(cancellationToken);
                stocks = stocks.Where(s => live.Contains(s.Symbol)).ToList();
            }

            var stockList = stocks as IList<Stock> ?? stocks.ToList();
            var total = stockList.Count;
            var synced = 0;
            var bars = 0;
            var done = 0;

            _logger.LogInformation(
                "Crypto history sync starting: {Total} symbols (full={Full})",
                total, request.FullRefresh);

            foreach (var stock in stockList)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = 0;
                try
                {
                    count = await SyncOneAsync(stock, request.FullRefresh, cancellationToken);
                    if (count > 0)
                    {
                        synced++;
                        bars += count;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Crypto history sync failed for {Symbol}", stock.Symbol);
                }
                finally
                {
                    done++;
                    _logger.LogInformation(
                        "Crypto history sync progress: {Done}/{Total} — {Symbol} ({Bars} bars)",
                        done, total, stock.Symbol, count);
                }
            }

            _logger.LogInformation(
                "Crypto history sync done: {Done}/{Total}, seeded={Seeded}, synced={Synced}, bars={Bars}",
                done, total, seeded, synced, bars);

            return new SyncCryptoHistoryResult(seeded, synced, bars, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Crypto history sync failed");
            return new SyncCryptoHistoryResult(0, 0, 0, ex.Message);
        }
    }

    private async Task<int> SyncOneAsync(Stock stock, bool fullRefresh, CancellationToken ct)
    {
        DateTime? start = null;
        if (!fullRefresh && stock.LatestDataDate.HasValue)
            start = stock.LatestDataDate.Value.Date.AddDays(-2); // overlap for corrections
        else if (fullRefresh)
            start = DateTime.UnixEpoch;

        var klines = await _binance.GetDailyKlinesAsync(stock.Symbol, start, endUtc: null, ct);
        if (klines.Count == 0) return 0;

        var now = DateTime.UtcNow;
        var records = klines
            .Where(k => k.Close > 0)
            .Select(k =>
            {
                var close = Math.Round(k.Close, 8);
                var vol = k.Volume > long.MaxValue ? long.MaxValue : (long)Math.Round(k.Volume);
                return new StockPriceHistory
                {
                    StockId = stock.Id,
                    Date = k.OpenTimeUtc.Date,
                    Open = Math.Round(k.Open > 0 ? k.Open : close, 8),
                    High = Math.Round(k.High > 0 ? k.High : close, 8),
                    Low = Math.Round(k.Low > 0 ? k.Low : close, 8),
                    Close = close,
                    AdjustedClose = close,
                    Volume = vol < 0 ? 0 : vol,
                    CreatedAt = now,
                };
            })
            .GroupBy(r => r.Date)
            .Select(g => g.Last())
            .OrderBy(r => r.Date)
            .ToList();

        if (records.Count == 0) return 0;

        var from = records[0].Date;
        var to = records[^1].Date;
        await _uow.PriceHistories.DeleteByStockIdAndDateRangeAsync(stock.Id, from, to, ct);
        await _uow.PriceHistories.BulkInsertAsync(records, ct);

        var earliest = await _uow.PriceHistories.GetEarliestByStockIdAsync(stock.Id, ct);
        var latest = await _uow.PriceHistories.GetLatestByStockIdAsync(stock.Id, ct);
        stock.EarliestDataDate = earliest?.Date;
        stock.LatestDataDate = latest?.Date;
        stock.NeedsHistoryRefresh = false;
        stock.UpdatedAt = now;
        _uow.Stocks.Update(stock);
        await _uow.SaveChangesAsync(ct);

        return records.Count;
    }

    private static string Normalize(string symbol)
    {
        var s = symbol.Trim().ToUpperInvariant();
        if (!s.EndsWith("USDT", StringComparison.Ordinal))
            s += "USDT";
        return s;
    }
}
