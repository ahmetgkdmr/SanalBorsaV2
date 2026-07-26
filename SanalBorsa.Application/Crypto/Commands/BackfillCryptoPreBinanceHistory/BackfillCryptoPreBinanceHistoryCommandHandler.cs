using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Crypto.Commands.BackfillCryptoPreBinanceHistory;

public sealed class BackfillCryptoPreBinanceHistoryCommandHandler
    : IRequestHandler<BackfillCryptoPreBinanceHistoryCommand, BackfillCryptoPreBinanceHistoryResult>
{
    private readonly IUnitOfWork _uow;
    private readonly IBinanceMarketClient _binance;
    private readonly IYahooFinanceService _yahoo;
    private readonly ICoinbaseMarketClient _coinbase;
    private readonly IZorinaqBitcoinArchiveClient _zorinaq;
    private readonly ILogger<BackfillCryptoPreBinanceHistoryCommandHandler> _logger;

    public BackfillCryptoPreBinanceHistoryCommandHandler(
        IUnitOfWork uow,
        IBinanceMarketClient binance,
        IYahooFinanceService yahoo,
        ICoinbaseMarketClient coinbase,
        IZorinaqBitcoinArchiveClient zorinaq,
        ILogger<BackfillCryptoPreBinanceHistoryCommandHandler> logger)
    {
        _uow = uow;
        _binance = binance;
        _yahoo = yahoo;
        _coinbase = coinbase;
        _zorinaq = zorinaq;
        _logger = logger;
    }

    public async Task<BackfillCryptoPreBinanceHistoryResult> Handle(
        BackfillCryptoPreBinanceHistoryCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var stocks = string.IsNullOrWhiteSpace(request.Symbol)
                ? await _uow.Stocks.GetAllActiveAsync(cancellationToken, MarketType.Crypto)
                : await _uow.Stocks.GetBySymbolsAsync(
                    [NormalizeUsdt(request.Symbol!)], cancellationToken, MarketType.Crypto);

            var stockList = stocks as IList<Stock> ?? stocks.ToList();
            if (stockList.Count == 0)
                return new BackfillCryptoPreBinanceHistoryResult(0, 0, "Crypto stock bulunamadı.");

            var coinbaseUsd = await _coinbase.GetUsdProductIdsAsync(cancellationToken);
            IReadOnlyList<ZorinaqDailyClose>? zorinaqCache = null;

            var processed = 0;
            var bars = 0;
            var done = 0;
            var total = stockList.Count;

            _logger.LogInformation(
                "Crypto pre-Binance USD backfill starting: {Total} symbols (Yahoo → Coinbase → Zorinaq/BTC)",
                total);

            async Task<IReadOnlyList<ZorinaqDailyClose>> GetZorinaqAsync()
            {
                if (zorinaqCache is not null) return zorinaqCache;
                zorinaqCache = await _zorinaq.GetDailyClosesAsync(cancellationToken);
                return zorinaqCache;
            }

            foreach (var stock in stockList.OrderBy(s => s.Symbol, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = 0;
                try
                {
                    count = await BackfillOneAsync(stock, coinbaseUsd, GetZorinaqAsync, cancellationToken);
                    if (count > 0)
                    {
                        processed++;
                        bars += count;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Pre-Binance backfill failed for {Symbol}", stock.Symbol);
                }
                finally
                {
                    done++;
                    _logger.LogInformation(
                        "Crypto pre-Binance backfill progress: {Done}/{Total} — {Symbol} ({Bars} bars)",
                        done, total, stock.Symbol, count);
                }

                await Task.Delay(120, cancellationToken);
            }

            _logger.LogInformation(
                "Crypto pre-Binance USD backfill done: processed={Processed}/{Total}, bars={Bars}",
                processed, total, bars);

            return new BackfillCryptoPreBinanceHistoryResult(processed, bars, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Crypto pre-Binance USD backfill failed");
            return new BackfillCryptoPreBinanceHistoryResult(0, 0, ex.Message);
        }
    }

    private async Task<int> BackfillOneAsync(
        Stock stock,
        IReadOnlySet<string> coinbaseUsd,
        Func<Task<IReadOnlyList<ZorinaqDailyClose>>> getZorinaq,
        CancellationToken ct)
    {
        var protectFrom = await _binance.GetFirstDailyKlineDateAsync(stock.Symbol, ct);
        if (!protectFrom.HasValue)
        {
            _logger.LogDebug("{Symbol}: Binance listing tarihi yok — atlandı", stock.Symbol);
            return 0;
        }

        var baseAsset = ResolveBaseAsset(stock);
        var candidates = BuildUsdCandidates(baseAsset);

        _logger.LogDebug(
            "Backfilling {Symbol} (base={Base}) before {Cutoff:yyyy-MM-dd}; candidates={Candidates}",
            stock.Symbol, baseAsset, protectFrom.Value, string.Join(",", candidates));

        var now = DateTime.UtcNow;
        var byDate = new SortedDictionary<DateTime, StockPriceHistory>();

        // 1) Zorinaq only BTC — en eski arşiv katmanı
        if (string.Equals(baseAsset, "BTC", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(stock.Symbol, "BTCUSDT", StringComparison.OrdinalIgnoreCase))
        {
            var archive = await getZorinaq();
            foreach (var b in archive.Where(x => x.Date < protectFrom.Value && x.Close > 0))
            {
                byDate[b.Date] = ToHistory(
                    stock.Id, b.Date, b.Close, b.Close, b.Close, b.Close, b.Close, 0, now);
            }
        }

        // 2) Coinbase USD
        foreach (var product in candidates.Where(coinbaseUsd.Contains))
        {
            var cb = await _coinbase.GetDailyUsdCandlesAsync(
                product,
                new DateTime(2010, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                protectFrom.Value,
                ct);
            foreach (var b in cb.Where(x => x.Date < protectFrom.Value && x.Close > 0))
            {
                var vol = b.Volume > long.MaxValue ? long.MaxValue : (long)Math.Round(b.Volume);
                byDate[b.Date] = ToHistory(
                    stock.Id, b.Date, b.Open, b.High, b.Low, b.Close, b.Close, vol, now);
            }
        }

        // 3) Yahoo USD (çakışmada Yahoo kazanır)
        string? usedYahoo = null;
        foreach (var yahooSym in candidates)
        {
            var yahooBars = await _yahoo.TryGetPriceHistoryAsync(
                yahooSym, DateTime.UnixEpoch, protectFrom.Value, ct);
            var usable = yahooBars.Where(x => x.Date.Date < protectFrom.Value && x.Close > 0).ToList();
            if (usable.Count == 0) continue;

            foreach (var r in usable)
            {
                byDate[r.Date.Date] = ToHistory(
                    stock.Id, r.Date.Date, r.Open, r.High, r.Low, r.Close, r.AdjustedClose, r.Volume, now);
            }
            usedYahoo = yahooSym;
            break;
        }

        if (byDate.Count == 0)
            return 0;

        var records = byDate.Values.ToList();
        var from = records[0].Date;
        var to = records[^1].Date;

        await _uow.PriceHistories.DeleteByStockIdAndDateRangeAsync(stock.Id, from, to, ct);
        await _uow.PriceHistories.BulkInsertAsync(records, ct);

        if (!string.IsNullOrWhiteSpace(usedYahoo) && string.IsNullOrWhiteSpace(stock.YahooSymbol))
            stock.YahooSymbol = usedYahoo;

        var earliest = await _uow.PriceHistories.GetEarliestByStockIdAsync(stock.Id, ct);
        var latest = await _uow.PriceHistories.GetLatestByStockIdAsync(stock.Id, ct);
        stock.EarliestDataDate = earliest?.Date;
        stock.LatestDataDate = latest?.Date;
        stock.UpdatedAt = now;
        _uow.Stocks.Update(stock);
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation(
            "{Symbol} pre-Binance filled: {Count} bars ({From:yyyy-MM-dd} → {To:yyyy-MM-dd}), listing={Listing:yyyy-MM-dd}, yahoo={Yahoo}",
            stock.Symbol, records.Count, from, to, protectFrom.Value, usedYahoo ?? "-");

        return records.Count;
    }

    private static string ResolveBaseAsset(Stock stock)
    {
        if (!string.IsNullOrWhiteSpace(stock.Name) &&
            !stock.Name.EndsWith("USDT", StringComparison.OrdinalIgnoreCase))
            return stock.Name.Trim().ToUpperInvariant();

        var s = stock.Symbol.Trim().ToUpperInvariant();
        return s.EndsWith("USDT", StringComparison.Ordinal) ? s[..^4] : s;
    }

    /// <summary>Yahoo/Coinbase adayları: BASE-USD, gerekirse 1000* öneksiz.</summary>
    private static List<string> BuildUsdCandidates(string baseAsset)
    {
        var list = new List<string>();
        void Add(string b)
        {
            if (string.IsNullOrWhiteSpace(b)) return;
            var id = $"{b.Trim().ToUpperInvariant()}-USD";
            if (!list.Contains(id, StringComparer.OrdinalIgnoreCase))
                list.Add(id);
        }

        Add(baseAsset);
        if (baseAsset.StartsWith("1000", StringComparison.OrdinalIgnoreCase) && baseAsset.Length > 4)
            Add(baseAsset[4..]);
        if (baseAsset.StartsWith("1000000", StringComparison.OrdinalIgnoreCase) && baseAsset.Length > 7)
            Add(baseAsset[7..]);

        return list;
    }

    private static StockPriceHistory ToHistory(
        int stockId,
        DateTime date,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        decimal adj,
        long volume,
        DateTime now)
    {
        var c = Math.Round(close, 8);
        return new StockPriceHistory
        {
            StockId = stockId,
            Date = date,
            Open = Math.Round(open > 0 ? open : c, 8),
            High = Math.Round(high > 0 ? high : c, 8),
            Low = Math.Round(low > 0 ? low : c, 8),
            Close = c,
            AdjustedClose = Math.Round(adj > 0 ? adj : c, 8),
            Volume = volume < 0 ? 0 : volume,
            CreatedAt = now,
        };
    }

    private static string NormalizeUsdt(string symbol)
    {
        var s = symbol.Trim().ToUpperInvariant();
        if (!s.EndsWith("USDT", StringComparison.Ordinal))
            s += "USDT";
        return s;
    }
}
