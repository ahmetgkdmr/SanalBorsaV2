using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Seeds;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Enums;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Stocks.Commands.ComputeTimeMachineLeaders;

/// <summary>
/// "O gün alsaydın bugün" tablosunu üretir.
/// </summary>
/// <remarks>
/// Getiri paydası sabit (bugünün kapanışı), payı ise her günün kapanışı olduğundan
/// hisse başına geçmişe tekrar tekrar gidilmez: bitiş fiyatları tek sorguda alınır,
/// ardından fiyat tablosu <b>tarih sırasıyla bir kez</b> taranır. Her tarih bloğu
/// bellekte sabit boyutlu bir top-5 tamponundan geçer, yani maliyet O(satır) ve
/// bellek O(bir günün hisse sayısı).
/// </remarks>
public class ComputeTimeMachineLeadersCommandHandler
    : IRequestHandler<ComputeTimeMachineLeadersCommand, ComputeTimeMachineLeadersResult>
{
    private const int TopN = 5;

    /// <summary>Fiyat tablosu bu uzunlukta dilimler hâlinde okunur (bellek sınırlı kalsın).</summary>
    private const int ChunkYears = 3;

    /// <summary>Bu tarihten önce hiçbir markette veri yok — tarama tabanı.</summary>
    private static readonly DateTime HistoryFloor = new(1985, 1, 1);

    /// <summary>Stablecoin / fiat çiftleri — "en çok kazandıran" listesinde yer almasınlar.</summary>
    private static readonly HashSet<string> CryptoStableBases = new(StringComparer.OrdinalIgnoreCase)
    {
        "USDC", "FDUSD", "TUSD", "BUSD", "USDP", "DAI", "USD1", "USDE", "USDS", "USDG",
        "PYUSD", "RLUSD", "XUSD", "EURI", "AEUR", "EURC", "EUR", "GBP", "JPY", "TRY", "BRL",
        "USDT",
    };

    private readonly IUnitOfWork _uow;
    private readonly ILogger<ComputeTimeMachineLeadersCommandHandler> _logger;

    public ComputeTimeMachineLeadersCommandHandler(
        IUnitOfWork uow,
        ILogger<ComputeTimeMachineLeadersCommandHandler> logger)
    {
        _uow = uow;
        _logger = logger;
    }

    public async Task<ComputeTimeMachineLeadersResult> Handle(
        ComputeTimeMachineLeadersCommand request,
        CancellationToken cancellationToken)
    {
        var total = Stopwatch.StartNew();

        var targets = request.Category is { } single
            ? new[] { single }
            : [TimeMachineCategory.Bist, TimeMachineCategory.Crypto, TimeMachineCategory.Parity];

        var results = new List<TimeMachineCategoryResult>(targets.Length);

        foreach (var category in targets)
        {
            results.Add(category == TimeMachineCategory.Parity
                ? await ComputeParityAsync(cancellationToken)
                : await ComputeMarketAsync(category, cancellationToken));
        }

        return new ComputeTimeMachineLeadersResult(results, total.ElapsedMilliseconds);
    }

    // ── BIST / Kripto: günlük top-5 ──────────────────────────────────────────

    private async Task<TimeMachineCategoryResult> ComputeMarketAsync(
        TimeMachineCategory category,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var market = category == TimeMachineCategory.Crypto ? MarketType.Crypto : MarketType.Bist;

        var stocks = (await _uow.Stocks.GetAllActiveAsync(ct, market))
            .Where(s => s.MarketType == market)
            .Where(s => !MarketInstrumentSeed.IsMarketInstrument(s.Exchange))
            .Where(s => market != MarketType.Crypto || !IsCryptoStable(s))
            .ToList();

        if (stocks.Count == 0)
            return await EmptyAsync(category, sw, "Kategoride aktif enstrüman yok.", ct);

        var asOf = await _uow.PriceHistories.GetLatestTradingDateForMarketAsync(market, ct);
        if (asOf is null)
            return await EmptyAsync(category, sw, "Fiyat geçmişi bulunamadı.", ct);

        var endDate = asOf.Value.Date;
        var byId = stocks.ToDictionary(s => s.Id);

        // Bitiş fiyatları — tek sorgu, tüm evren için.
        var endCloses = await _uow.PriceHistories.GetClosesOnOrBeforeAsync(
            stocks.Select(s => s.Id).ToList(), endDate, ct);

        // Kotasyondan çıkmış / veri akışı durmuş semboller "bugün elimde olurdu" diyemez.
        var staleCutoff = endDate.AddDays(market == MarketType.Crypto ? -3 : -10);
        var end = new Dictionary<int, decimal>(endCloses.Count);
        foreach (var (stockId, snapshot) in endCloses)
        {
            if (snapshot.Date >= staleCutoff && snapshot.Close > 0m)
                end[stockId] = snapshot.Close;
        }

        if (end.Count == 0)
            return await EmptyAsync(category, sw, "Güncel kapanışı olan enstrüman yok.", ct);

        var scanFrom = stocks
            .Select(s => s.EarliestDataDate)
            .Where(d => d.HasValue)
            .Select(d => d!.Value.Date)
            .DefaultIfEmpty(HistoryFloor)
            .Min();

        if (scanFrom < HistoryFloor)
            scanFrom = HistoryFloor;

        var rows = new List<TimeMachineLeader>();
        var buffer = new TopBuffer(TopN);
        var computedAt = DateTime.UtcNow;
        var days = 0;
        DateTime? earliestStart = null;

        for (var chunkFrom = scanFrom; chunkFrom <= endDate; chunkFrom = chunkFrom.AddYears(ChunkYears))
        {
            var chunkTo = chunkFrom.AddYears(ChunkYears).AddDays(-1);
            if (chunkTo > endDate) chunkTo = endDate;

            var closes = await _uow.PriceHistories.GetDailyClosesAsync(market, chunkFrom, chunkTo, ct);

            var i = 0;
            while (i < closes.Count)
            {
                var date = closes[i].Date.Date;
                buffer.Reset();

                while (i < closes.Count && closes[i].Date.Date == date)
                {
                    var row = closes[i++];
                    if (row.Close <= 0m || !end.TryGetValue(row.StockId, out var endClose))
                        continue;

                    buffer.Offer(new Candidate(
                        row.StockId,
                        byId[row.StockId].Symbol,
                        row.Close,
                        endClose,
                        (endClose - row.Close) / row.Close * 100m));
                }

                // Alım günü = bitiş günü ise getiri sıfır; anlamlı bir "alternatif" değil.
                if (date >= endDate || buffer.Count == 0)
                    continue;

                days++;
                earliestStart ??= date;

                for (var rank = 0; rank < buffer.Count; rank++)
                {
                    var c = buffer[rank];
                    var stock = byId[c.StockId];
                    rows.Add(new TimeMachineLeader
                    {
                        Category = category,
                        StartDate = date,
                        Rank = rank + 1,
                        StockId = c.StockId,
                        Symbol = stock.Symbol,
                        Name = stock.Name,
                        StartPrice = c.StartPrice,
                        EndPrice = c.EndPrice,
                        ReturnPct = Math.Round(c.ReturnPct, 4),
                        EndDate = endDate,
                        ComputedAt = computedAt,
                    });
                }
            }
        }

        await _uow.TimeMachineLeaders.ReplaceCategoryAsync(category, rows, ct);

        _logger.LogInformation(
            "TimeMachineLeaders {Category}: {Days} gün / {Rows} satır — evren {Universe}, bitiş {EndDate:yyyy-MM-dd}, {Elapsed} ms",
            category, days, rows.Count, end.Count, endDate, sw.ElapsedMilliseconds);

        return new TimeMachineCategoryResult(
            category, days, rows.Count, earliestStart, endDate, sw.ElapsedMilliseconds, null);
    }

    // ── Pariteler: her gün USD/TRY, EUR/TRY, gram altın ──────────────────────

    private async Task<TimeMachineCategoryResult> ComputeParityAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        const TimeMachineCategory category = TimeMachineCategory.Parity;

        var tracks = new List<ParityTrack>();
        var rank = 0;

        foreach (var symbol in MarketInstrumentSeed.ParitySymbols)
        {
            rank++;
            var stock = await _uow.Stocks.GetBySymbolAsync(symbol, ct);
            if (stock is null)
            {
                _logger.LogWarning("Parite enstrümanı yok: {Symbol}", symbol);
                continue;
            }

            var prices = await _uow.PriceHistories.GetByStockIdAsync(stock.Id, ct: ct);
            if (prices.Count == 0)
            {
                _logger.LogWarning("Parite fiyat geçmişi boş: {Symbol}", symbol);
                continue;
            }

            tracks.Add(new ParityTrack(stock, prices, rank));
        }

        if (tracks.Count == 0)
            return await EmptyAsync(category, sw, "Parite verisi yok.", ct);

        // Gün evreni: üç serinin işlem günlerinin birleşimi. Bir parite o gün kapalıysa
        // son kapanışı taşınır (tatilde de "o gün dolar alsaydın" cevaplanabilsin).
        var dates = new SortedSet<DateTime>();
        foreach (var track in tracks)
            foreach (var price in track.Prices)
                dates.Add(price.Date.Date);

        var cursor = new int[tracks.Count];
        var carried = new decimal?[tracks.Count];
        var rows = new List<TimeMachineLeader>(dates.Count * tracks.Count);
        var computedAt = DateTime.UtcNow;
        var days = 0;
        DateTime? earliestStart = null;
        DateTime? maxEnd = null;

        foreach (var date in dates)
        {
            var emitted = false;

            for (var k = 0; k < tracks.Count; k++)
            {
                var prices = tracks[k].Prices;
                while (cursor[k] < prices.Count && prices[cursor[k]].Date.Date <= date)
                    carried[k] = prices[cursor[k]++].Close;

                var track = tracks[k];
                if (date >= track.EndDate) continue;

                var startPrice = carried[k];
                if (startPrice is null || startPrice.Value <= 0m) continue;

                rows.Add(new TimeMachineLeader
                {
                    Category = category,
                    StartDate = date,
                    Rank = track.Rank,
                    StockId = track.Stock.Id,
                    Symbol = track.Stock.Symbol,
                    Name = track.Stock.Name,
                    StartPrice = startPrice.Value,
                    EndPrice = track.EndPrice,
                    ReturnPct = Math.Round(
                        (track.EndPrice - startPrice.Value) / startPrice.Value * 100m, 4),
                    EndDate = track.EndDate,
                    ComputedAt = computedAt,
                });

                emitted = true;
                if (maxEnd is null || track.EndDate > maxEnd) maxEnd = track.EndDate;
            }

            if (!emitted) continue;
            days++;
            earliestStart ??= date;
        }

        await _uow.TimeMachineLeaders.ReplaceCategoryAsync(category, rows, ct);

        _logger.LogInformation(
            "TimeMachineLeaders Parity: {Days} gün / {Rows} satır — {Symbols}, {Elapsed} ms",
            days, rows.Count, string.Join(", ", tracks.Select(t => t.Stock.Symbol)), sw.ElapsedMilliseconds);

        return new TimeMachineCategoryResult(
            category, days, rows.Count, earliestStart, maxEnd, sw.ElapsedMilliseconds, null);
    }

    private async Task<TimeMachineCategoryResult> EmptyAsync(
        TimeMachineCategory category,
        Stopwatch sw,
        string error,
        CancellationToken ct)
    {
        await _uow.TimeMachineLeaders.ReplaceCategoryAsync(category, [], ct);
        _logger.LogWarning("TimeMachineLeaders {Category} atlandı: {Error}", category, error);
        return new TimeMachineCategoryResult(category, 0, 0, null, null, sw.ElapsedMilliseconds, error);
    }

    private static bool IsCryptoStable(Stock stock)
    {
        var baseAsset = !string.IsNullOrWhiteSpace(stock.Name)
            ? stock.Name.Trim().ToUpperInvariant()
            : stock.Symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase)
                ? stock.Symbol[..^4]
                : stock.Symbol;
        return CryptoStableBases.Contains(baseAsset);
    }

    private sealed record ParityTrack(
        Stock Stock,
        IReadOnlyList<StockPriceHistory> Prices,
        int Rank)
    {
        public DateTime EndDate { get; } = Prices[^1].Date.Date;

        public decimal EndPrice { get; } = Prices[^1].Close;
    }

    private readonly record struct Candidate(
        int StockId,
        string Symbol,
        decimal StartPrice,
        decimal EndPrice,
        decimal ReturnPct);

    /// <summary>
    /// Sabit kapasiteli, sıralı tutulan top-K tamponu. Gün başına tam sıralama yapmak
    /// yerine O(aday × K) yerleştirme yapar ve hiç ek tahsis üretmez.
    /// </summary>
    private sealed class TopBuffer
    {
        private readonly Candidate[] _items;

        public TopBuffer(int capacity) => _items = new Candidate[capacity];

        public int Count { get; private set; }

        public Candidate this[int index] => _items[index];

        public void Reset() => Count = 0;

        public void Offer(in Candidate candidate)
        {
            if (Count == _items.Length && !IsBetter(candidate, _items[Count - 1]))
                return;

            var pos = Count < _items.Length ? Count : _items.Length - 1;
            while (pos > 0 && IsBetter(candidate, _items[pos - 1]))
            {
                _items[pos] = _items[pos - 1];
                pos--;
            }

            _items[pos] = candidate;
            if (Count < _items.Length) Count++;
        }

        private static bool IsBetter(in Candidate a, in Candidate b)
            => a.ReturnPct > b.ReturnPct
               || (a.ReturnPct == b.ReturnPct && string.CompareOrdinal(a.Symbol, b.Symbol) < 0);
    }
}
