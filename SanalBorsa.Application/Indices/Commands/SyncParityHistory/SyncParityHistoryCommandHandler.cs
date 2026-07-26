using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Application.Common.Seeds;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Indices.Commands.SyncParityHistory;

/// <summary>
/// USD/TRY ve EUR/TRY doğrudan Yahoo'dan; gram altın GC=F (USD/ons) × USD/TRY ÷ 31,1034768
/// ile türetilip aynı fiyat tablosuna yazılır. Böylece zaman makinesi ve liderlik hesabı
/// pariteleri de sıradan bir enstrüman gibi kullanabilir.
/// </summary>
public class SyncParityHistoryCommandHandler
    : IRequestHandler<SyncParityHistoryCommand, SyncParityHistoryResult>
{
    /// <summary>Artımlı çekimde son günleri yeniden yazmak için geriye dönük pencere.</summary>
    private const int OverlapDays = 7;

    private readonly IUnitOfWork _uow;
    private readonly IYahooFinanceService _yahoo;
    private readonly ILogger<SyncParityHistoryCommandHandler> _logger;

    public SyncParityHistoryCommandHandler(
        IUnitOfWork uow,
        IYahooFinanceService yahoo,
        ILogger<SyncParityHistoryCommandHandler> logger)
    {
        _uow = uow;
        _yahoo = yahoo;
        _logger = logger;
    }

    public async Task<SyncParityHistoryResult> Handle(
        SyncParityHistoryCommand request,
        CancellationToken cancellationToken)
    {
        var details = new List<ParitySyncDetail>
        {
            // Gram altın USD/TRY'ye bağlı — kur önce tazelenmeli.
            await SyncFromYahooAsync("USDTRY", request.Full, cancellationToken),
            await SyncFromYahooAsync("EURTRY", request.Full, cancellationToken),
        };

        details.Add(await SyncGramGoldAsync(request.Full, cancellationToken));

        return new SyncParityHistoryResult(details);
    }

    private async Task<ParitySyncDetail> SyncFromYahooAsync(
        string symbol,
        bool full,
        CancellationToken ct)
    {
        var entry = MarketInstrumentSeed.FindBySymbol(symbol);
        if (entry is null || string.IsNullOrWhiteSpace(entry.YahooSymbol))
            return new ParitySyncDetail(symbol, 0, null, null, "Seed kaydı yok.");

        try
        {
            var stock = await EnsureStockAsync(entry, ct);
            var from = ResolveFrom(stock, full);

            var bars = await _yahoo.GetPriceHistoryAsync(
                entry.YahooSymbol, from, DateTime.UtcNow.AddDays(1), ct);

            return await WriteBarsAsync(stock, bars, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Parite senkronu başarısız: {Symbol}", symbol);
            return new ParitySyncDetail(symbol, 0, null, null, ex.Message);
        }
    }

    private async Task<ParitySyncDetail> SyncGramGoldAsync(bool full, CancellationToken ct)
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

            var from = ResolveFrom(stock, full);

            var ounce = await _yahoo.GetPriceHistoryAsync(
                MarketInstrumentSeed.GoldOunceYahooSymbol, from, DateTime.UtcNow.AddDays(1), ct);

            if (ounce.Count == 0)
                return new ParitySyncDetail(symbol, 0, null, null, "GC=F verisi boş döndü.");

            // Kur serisi ons serisinden en az bir ay önce başlasın; tatil günlerinde son kur taşınır.
            var rates = await _uow.PriceHistories.GetByStockIdAsync(
                usd.Id, from: ounce[0].Date.AddDays(-45), ct: ct);

            var bars = DeriveGramGold(ounce, rates);
            if (bars.Count == 0)
                return new ParitySyncDetail(symbol, 0, null, null, "Eşleşen USD/TRY kuru bulunamadı.");

            return await WriteBarsAsync(stock, bars, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gram altın türetmesi başarısız");
            return new ParitySyncDetail(symbol, 0, null, null, ex.Message);
        }
    }

    /// <summary>Ons/USD barlarını, o güne kadarki son USD/TRY kuruyla gram/TL'ye çevirir.</summary>
    private static List<StockPriceHistory> DeriveGramGold(
        IReadOnlyList<StockPriceHistory> ounceBars,
        IReadOnlyList<StockPriceHistory> usdRates)
    {
        var ordered = ounceBars.OrderBy(b => b.Date).ToList();
        var bars = new List<StockPriceHistory>(ordered.Count);
        var now = DateTime.UtcNow;

        var rateIdx = 0;
        decimal? rate = null;

        foreach (var bar in ordered)
        {
            while (rateIdx < usdRates.Count && usdRates[rateIdx].Date.Date <= bar.Date.Date)
                rate = usdRates[rateIdx++].Close;

            if (rate is null || rate.Value <= 0m || bar.Close <= 0m)
                continue;

            var factor = rate.Value / MarketInstrumentSeed.GramsPerTroyOunce;

            bars.Add(new StockPriceHistory
            {
                Date = bar.Date.Date,
                Open = Math.Round(bar.Open * factor, 4),
                High = Math.Round(bar.High * factor, 4),
                Low = Math.Round(bar.Low * factor, 4),
                Close = Math.Round(bar.Close * factor, 4),
                AdjustedClose = Math.Round(bar.Close * factor, 4),
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

        // Sadece gelen aralık silinir; USD/TRY'nin TradingView'dan gelen 1989+ geçmişi korunur.
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
            "Parite {Symbol}: {Rows} satır yazıldı ({From:yyyy-MM-dd} → {To:yyyy-MM-dd}), seri {Earliest:yyyy-MM-dd} → {Latest:yyyy-MM-dd}",
            stock.Symbol, bars.Count, rangeFrom, rangeTo, stock.EarliestDataDate, stock.LatestDataDate);

        return new ParitySyncDetail(
            stock.Symbol, bars.Count, stock.EarliestDataDate, stock.LatestDataDate, null);
    }

    private static DateTime ResolveFrom(Stock stock, bool full)
        => full || stock.LatestDataDate is null
            ? DateTime.UnixEpoch
            : stock.LatestDataDate.Value.AddDays(-OverlapDays);

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
