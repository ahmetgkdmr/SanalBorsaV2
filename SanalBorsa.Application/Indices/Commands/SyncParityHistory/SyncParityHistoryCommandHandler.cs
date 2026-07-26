using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Application.Common.Seeds;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Indices.Commands.SyncParityHistory;

/// <summary>
/// Parite geçmişi — en erken BIST tarihinden bugüne.
/// Kaynak sırası: TCMB → TradingView → Yahoo (eksik günler birleştirilir).
/// EUR erken dönem: EURUSD×USDTRY; altın: XAUUSD×USDTRY (veya TCMB XAU).
/// </summary>
public class SyncParityHistoryCommandHandler
    : IRequestHandler<SyncParityHistoryCommand, SyncParityHistoryResult>
{
    private const int OverlapDays = 7;
    private static readonly DateTime HardFloor = new(1985, 1, 1);

    private readonly IUnitOfWork _uow;
    private readonly IYahooFinanceService _yahoo;
    private readonly ITradingViewHistoryService _tv;
    private readonly ITcmbFxHistoryService _tcmb;
    private readonly ILogger<SyncParityHistoryCommandHandler> _logger;

    public SyncParityHistoryCommandHandler(
        IUnitOfWork uow,
        IYahooFinanceService yahoo,
        ITradingViewHistoryService tv,
        ITcmbFxHistoryService tcmb,
        ILogger<SyncParityHistoryCommandHandler> logger)
    {
        _uow = uow;
        _yahoo = yahoo;
        _tv = tv;
        _tcmb = tcmb;
        _logger = logger;
    }

    public async Task<SyncParityHistoryResult> Handle(
        SyncParityHistoryCommand request,
        CancellationToken cancellationToken)
    {
        var floor = await ResolveHistoryFloorAsync(cancellationToken);
        _logger.LogInformation(
            "Parite sync floor={Floor:yyyy-MM-dd} full={Full} (TCMB→TV→Yahoo)",
            floor, request.Full);

        var to = DateTime.UtcNow.Date.AddDays(1);
        var tcmbFrom = request.Full
            ? floor
            : (DateTime.UtcNow.Date.AddDays(-30) < floor ? floor : DateTime.UtcNow.Date.AddDays(-30));

        TcmbFxBundle tcmb;
        try
        {
            tcmb = await _tcmb.GetParityBundleAsync(tcmbFrom, to, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TCMB bundle başarısız — TV/Yahoo ile devam");
            tcmb = new TcmbFxBundle([], [], []);
        }

        var usdDetail = await SyncUsdTryAsync(floor, request.Full, tcmb.UsdTry, cancellationToken);
        var eurDetail = await SyncEurTryAsync(floor, request.Full, tcmb.EurTry, cancellationToken);
        var goldDetail = await SyncGramGoldAsync(floor, request.Full, tcmb.GramGold, cancellationToken);

        return new SyncParityHistoryResult([usdDetail, eurDetail, goldDetail]);
    }

    private async Task<DateTime> ResolveHistoryFloorAsync(CancellationToken ct)
    {
        var stocks = await _uow.Stocks.GetAllActiveAsync(ct, MarketType.Bist);
        var earliest = stocks
            .Where(s => !MarketInstrumentSeed.IsMarketInstrument(s.Exchange))
            .Select(s => s.EarliestDataDate)
            .Where(d => d.HasValue)
            .Select(d => d!.Value.Date)
            .DefaultIfEmpty(HardFloor)
            .Min();

        return earliest < HardFloor ? HardFloor : earliest;
    }

    private async Task<ParitySyncDetail> SyncUsdTryAsync(
        DateTime floor,
        bool full,
        IReadOnlyList<StockPriceHistory> tcmbUsd,
        CancellationToken ct)
    {
        const string symbol = "USDTRY";
        try
        {
            var entry = MarketInstrumentSeed.FindBySymbol(symbol)!;
            var stock = await EnsureStockAsync(entry, ct);
            var from = ResolveFrom(stock, floor, full);
            var to = DateTime.UtcNow.Date.AddDays(1);

            var merged = new Dictionary<DateTime, StockPriceHistory>();
            MergeInto(merged, tcmbUsd.Where(b => b.Date >= from).ToList(), prefer: true);
            MergeInto(merged, await _tv.GetDailyBarsByTvSymbolAsync(
                MarketInstrumentSeed.UsdTryTvSymbol, from, to, ct), prefer: false);
            if (merged.Count == 0 || NeedsMoreHistory(merged, from))
                MergeInto(merged, await SafeYahooAsync(entry.YahooSymbol, from, to, ct), prefer: false);

            return await WriteBarsAsync(stock, merged.Values.OrderBy(b => b.Date).ToList(), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Parite senkronu başarısız: {Symbol}", symbol);
            return new ParitySyncDetail(symbol, 0, null, null, ex.Message);
        }
    }

    private async Task<ParitySyncDetail> SyncEurTryAsync(
        DateTime floor,
        bool full,
        IReadOnlyList<StockPriceHistory> tcmbEur,
        CancellationToken ct)
    {
        const string symbol = "EURTRY";
        try
        {
            var entry = MarketInstrumentSeed.FindBySymbol(symbol)!;
            var stock = await EnsureStockAsync(entry, ct);
            var from = ResolveFrom(stock, floor, full);
            var to = DateTime.UtcNow.Date.AddDays(1);

            var merged = new Dictionary<DateTime, StockPriceHistory>();
            MergeInto(merged, tcmbEur.Where(b => b.Date >= from).ToList(), prefer: true);
            MergeInto(merged, await _tv.GetDailyBarsByTvSymbolAsync(
                MarketInstrumentSeed.EurTryTvSymbol, from, to, ct), prefer: false);

            var usd = await _uow.Stocks.GetBySymbolAsync("USDTRY", ct);
            if (usd is not null)
            {
                var eurusd = await _tv.GetDailyBarsByTvSymbolAsync(
                    MarketInstrumentSeed.EurUsdTvSymbol, from, to, ct);
                if (eurusd.Count > 0)
                {
                    var rates = await _uow.PriceHistories.GetByStockIdAsync(
                        usd.Id, from: from.AddDays(-60), ct: ct);
                    MergeInto(merged, MultiplyFx(eurusd, rates, decimals: 4), prefer: false);
                }
            }

            if (NeedsMoreHistory(merged, from))
                MergeInto(merged, await SafeYahooAsync(entry.YahooSymbol, from, to, ct), prefer: false);

            return await WriteBarsAsync(stock, merged.Values.OrderBy(b => b.Date).ToList(), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Parite senkronu başarısız: {Symbol}", symbol);
            return new ParitySyncDetail(symbol, 0, null, null, ex.Message);
        }
    }

    private async Task<ParitySyncDetail> SyncGramGoldAsync(
        DateTime floor,
        bool full,
        IReadOnlyList<StockPriceHistory> tcmbGold,
        CancellationToken ct)
    {
        const string symbol = "GRAMALTIN";
        var entry = MarketInstrumentSeed.FindBySymbol(symbol);
        if (entry is null)
            return new ParitySyncDetail(symbol, 0, null, null, "Seed kaydı yok.");

        try
        {
            var stock = await EnsureStockAsync(entry, ct);
            var usd = await _uow.Stocks.GetBySymbolAsync("USDTRY", ct);
            if (usd is null)
                return new ParitySyncDetail(symbol, 0, null, null, "USDTRY enstrümanı yok.");

            var from = ResolveFrom(stock, floor, full);
            var to = DateTime.UtcNow.Date.AddDays(1);
            var merged = new Dictionary<DateTime, StockPriceHistory>();

            MergeInto(merged, tcmbGold.Where(b => b.Date >= from).ToList(), prefer: true);

            var ounce = await _tv.GetDailyBarsByTvSymbolAsync(
                MarketInstrumentSeed.XauUsdTvSymbol, from, to, ct);
            if (ounce.Count == 0)
                ounce = await SafeYahooAsync(MarketInstrumentSeed.GoldOunceYahooSymbol, from, to, ct);

            if (ounce.Count > 0)
            {
                var rates = await _uow.PriceHistories.GetByStockIdAsync(
                    usd.Id, from: from.AddDays(-45), ct: ct);
                MergeInto(merged, MultiplyFx(ounce, rates, decimals: 2, divideByGrams: true), prefer: false);
            }

            if (merged.Count == 0)
                return new ParitySyncDetail(symbol, 0, null, null, "Altın verisi hiçbir kaynaktan gelmedi.");

            return await WriteBarsAsync(stock, merged.Values.OrderBy(b => b.Date).ToList(), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gram altın türetmesi başarısız");
            return new ParitySyncDetail(symbol, 0, null, null, ex.Message);
        }
    }

    private async Task<IReadOnlyList<StockPriceHistory>> SafeYahooAsync(
        string yahooSymbol, DateTime from, DateTime to, CancellationToken ct)
    {
        try
        {
            return await _yahoo.GetPriceHistoryAsync(yahooSymbol, from, to, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Yahoo {Symbol} başarısız", yahooSymbol);
            return [];
        }
    }

    /// <summary>prefer=true olan kaynak mevcut günü ezer (TCMB öncelikli).</summary>
    private static void MergeInto(
        Dictionary<DateTime, StockPriceHistory> target,
        IReadOnlyList<StockPriceHistory> bars,
        bool prefer)
    {
        foreach (var bar in bars)
        {
            var d = bar.Date.Date;
            if (prefer || !target.ContainsKey(d))
                target[d] = bar;
        }
    }

    private static bool NeedsMoreHistory(Dictionary<DateTime, StockPriceHistory> merged, DateTime floor)
    {
        if (merged.Count == 0) return true;
        var earliest = merged.Keys.Min();
        return earliest > floor.AddDays(30);
    }

    private static List<StockPriceHistory> MultiplyFx(
        IReadOnlyList<StockPriceHistory> foreignUsdBars,
        IReadOnlyList<StockPriceHistory> usdTryRates,
        int decimals,
        bool divideByGrams = false)
    {
        var ordered = foreignUsdBars.OrderBy(b => b.Date).ToList();
        var bars = new List<StockPriceHistory>(ordered.Count);
        var now = DateTime.UtcNow;
        var rateIdx = 0;
        decimal? rate = null;

        foreach (var bar in ordered)
        {
            while (rateIdx < usdTryRates.Count && usdTryRates[rateIdx].Date.Date <= bar.Date.Date)
                rate = usdTryRates[rateIdx++].Close;

            if (rate is null || rate.Value <= 0m || bar.Close <= 0m)
                continue;

            var factor = divideByGrams
                ? rate.Value / MarketInstrumentSeed.GramsPerTroyOunce
                : rate.Value;

            bars.Add(new StockPriceHistory
            {
                Date = bar.Date.Date,
                Open = Math.Round(bar.Open * factor, decimals),
                High = Math.Round(bar.High * factor, decimals),
                Low = Math.Round(bar.Low * factor, decimals),
                Close = Math.Round(bar.Close * factor, decimals),
                AdjustedClose = Math.Round(bar.Close * factor, decimals),
                Volume = 0,
                CreatedAt = now,
            });
        }

        return bars;
    }

    private async Task<ParitySyncDetail> WriteBarsAsync(
        Stock stock,
        IReadOnlyList<StockPriceHistory> bars,
        CancellationToken ct)
    {
        if (bars.Count == 0)
        {
            return new ParitySyncDetail(
                stock.Symbol, 0, stock.EarliestDataDate, stock.LatestDataDate, "Veri gelmedi.");
        }

        var rangeFrom = bars.Min(b => b.Date).Date;
        var rangeTo = bars.Max(b => b.Date).Date;

        await _uow.PriceHistories.DeleteByStockIdAndDateRangeAsync(stock.Id, rangeFrom, rangeTo, ct);

        foreach (var bar in bars)
            bar.StockId = stock.Id;

        await _uow.PriceHistories.BulkInsertAsync(bars, ct);

        var earliest = await _uow.PriceHistories.GetEarliestByStockIdAsync(stock.Id, ct);
        var latest = await _uow.PriceHistories.GetLatestByStockIdAsync(stock.Id, ct);

        stock.EarliestDataDate = earliest?.Date;
        stock.LatestDataDate = latest?.Date;
        stock.NeedsHistoryRefresh = false;
        stock.UpdatedAt = DateTime.UtcNow;
        _uow.Stocks.Update(stock);
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Parite {Symbol}: {Rows} satır ({From:yyyy-MM-dd} → {To:yyyy-MM-dd}), seri {Earliest:yyyy-MM-dd} → {Latest:yyyy-MM-dd}",
            stock.Symbol, bars.Count, rangeFrom, rangeTo, stock.EarliestDataDate, stock.LatestDataDate);

        return new ParitySyncDetail(
            stock.Symbol, bars.Count, stock.EarliestDataDate, stock.LatestDataDate, null);
    }

    private static DateTime ResolveFrom(Stock stock, DateTime floor, bool full)
    {
        if (full || stock.LatestDataDate is null || stock.EarliestDataDate is null
            || stock.EarliestDataDate.Value.Date > floor)
            return floor;

        var incremental = stock.LatestDataDate.Value.AddDays(-OverlapDays);
        return incremental < floor ? floor : incremental;
    }

    private async Task<Stock> EnsureStockAsync(MarketInstrumentEntry entry, CancellationToken ct)
    {
        var stock = await _uow.Stocks.GetBySymbolAsync(entry.Symbol, ct);
        if (stock is not null)
            return stock;

        stock = new Stock
        {
            Symbol = entry.Symbol,
            YahooSymbol = entry.YahooSymbol,
            Name = entry.Name,
            Currency = entry.Currency,
            Exchange = entry.Exchange,
            MarketType = MarketType.Bist,
            IsActive = true,
            NeedsHistoryRefresh = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        await _uow.Stocks.AddAsync(stock, ct);
        await _uow.SaveChangesAsync(ct);
        _logger.LogInformation("Parite enstrümanı oluşturuldu: {Symbol}", entry.Symbol);
        return stock;
    }
}
