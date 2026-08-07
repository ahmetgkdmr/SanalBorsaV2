using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Seeds;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Enums;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Stocks.Commands.ComputeTimeMachineLeaders;

/// <summary>
/// "O gün alsaydın bugün" tablosu.
/// BIST / ABD: <c>AdjustedClose</c> (TV dividends/split) oranı; ham Close yalnızca görüntü.
/// Kripto / parite: ham Close oranı.
/// </summary>
public class ComputeTimeMachineLeadersCommandHandler
    : IRequestHandler<ComputeTimeMachineLeadersCommand, ComputeTimeMachineLeadersResult>
{
    private const int TopN = 5;
    // 3 yıl × ~650 hisse × AdjustedClose sonrası tablo şişince SQL 30 sn timeout yiyordu
    private const int ChunkYears = 1;
    private static readonly DateTime HistoryFloor = new(1985, 1, 1);

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
            : [TimeMachineCategory.Bist, TimeMachineCategory.Crypto, TimeMachineCategory.UsStocks, TimeMachineCategory.Parity];

        var results = new List<TimeMachineCategoryResult>(targets.Length);
        foreach (var category in targets)
        {
            results.Add(category == TimeMachineCategory.Parity
                ? await ComputeParityAsync(cancellationToken)
                : await ComputeMarketAsync(category, cancellationToken));
        }

        return new ComputeTimeMachineLeadersResult(results, total.ElapsedMilliseconds);
    }

    private async Task<TimeMachineCategoryResult> ComputeMarketAsync(
        TimeMachineCategory category,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var market = category switch
        {
            TimeMachineCategory.Crypto => MarketType.Crypto,
            TimeMachineCategory.UsStocks => MarketType.UsStocks,
            _ => MarketType.Bist,
        };
        var useAdjusted = category is TimeMachineCategory.Bist or TimeMachineCategory.UsStocks;

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

        var endCloses = await _uow.PriceHistories.GetClosesOnOrBeforeAsync(
            stocks.Select(s => s.Id).ToList(), endDate, ct);

        var staleCutoff = endDate.AddDays(market == MarketType.Crypto ? -3 : -10);

        // Bitiş: BIST için AdjustedClose tercih (yoksa Close)
        var endRaw = new Dictionary<int, decimal>();
        var endRet = new Dictionary<int, decimal>();
        foreach (var (stockId, snapshot) in endCloses)
        {
            if (snapshot.Date < staleCutoff || snapshot.Close <= 0m)
                continue;

            endRaw[stockId] = snapshot.Close;
            // GetClosesOnOrBefore only returns Close — need AdjustedClose for end.
            // Fall back: load from a one-day scan below via first chunk, or extend GetClosesOnOrBefore.
            endRet[stockId] = snapshot.Close;
        }

        if (endRaw.Count == 0)
            return await EmptyAsync(category, sw, "Güncel kapanışı olan enstrüman yok.", ct);

        // BIST: bitiş AdjustedClose'u DailyClose tarama sonunda netleşir — önce end map'i
        // Close ile doldurduk; ilk chunk'ta adj varsa güncellenir. Daha temiz: end date satırlarını çek.
        if (useAdjusted)
        {
            var endDayBars = await _uow.PriceHistories.GetDailyClosesAsync(
                market, endDate.AddDays(-14), endDate, ct);
            foreach (var g in endDayBars.GroupBy(b => b.StockId))
            {
                var last = g.OrderByDescending(x => x.Date).First();
                if (!endRaw.ContainsKey(last.StockId))
                    continue;
                var retPx = last.AdjustedClose > 0m ? last.AdjustedClose : last.Close;
                if (retPx > 0m)
                    endRet[last.StockId] = retPx;
                endRaw[last.StockId] = last.Close > 0m ? last.Close : endRaw[last.StockId];
            }
        }

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
                    if (!endRet.TryGetValue(row.StockId, out var endRetPx) ||
                        !endRaw.TryGetValue(row.StockId, out var endRawPx))
                        continue;

                    var startRet = useAdjusted && row.AdjustedClose > 0m
                        ? row.AdjustedClose
                        : row.Close;
                    if (startRet <= 0m || row.Close <= 0m)
                        continue;

                    buffer.Offer(new Candidate(
                        row.StockId,
                        byId[row.StockId].Symbol,
                        row.Close,
                        endRawPx,
                        (endRetPx - startRet) / startRet * 100m));
                }

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
                        // 4 ondalığa yuvarlama önceden büyük çarpanlarda (ör. 40x+) TL bazında
                        // birkaç liralık farka büyüyordu — tek-hisse simülasyonuyla (ham AdjustedClose
                        // oranı, yuvarlamasız) birebir eşleşmesi için burada da tam hassasiyet korunuyor.
                        ReturnPct = c.ReturnPct,
                        EndDate = endDate,
                        ComputedAt = computedAt,
                    });
                }
            }
        }

        await _uow.TimeMachineLeaders.ReplaceCategoryAsync(category, rows, ct);

        _logger.LogInformation(
            "TimeMachineLeaders {Category}: {Days} gün / {Rows} satır — evren {Universe}, bitiş {EndDate:yyyy-MM-dd}, adj={Adj}, {Elapsed} ms",
            category, days, rows.Count, endRet.Count, endDate, useAdjusted, sw.ElapsedMilliseconds);

        return new TimeMachineCategoryResult(
            category, days, rows.Count, earliestStart, endDate, sw.ElapsedMilliseconds, null);
    }

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
