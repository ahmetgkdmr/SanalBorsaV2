using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common;
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
    /// <summary>"Günün en çok kaybettirenleri" — negatif Rank (-1..-LossTopN) ile aynı tabloda saklanır.</summary>
    private const int LossTopN = 3;
    // 3 yıl × ~650 hisse × AdjustedClose sonrası tablo şişince SQL 30 sn timeout yiyordu
    private const int ChunkYears = 1;
    private static readonly DateTime HistoryFloor = new(1985, 1, 1);

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
            .Where(s => market != MarketType.Crypto || !CryptoStableAssets.IsStable(s))
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

        // ABD/Kripto satırları TL bazında değerlendirilir — dolar cinsinden getiri (ör. bir hisse
        // %75 düşmüş) TEK BAŞINA TL yatırımcısının gerçek sonucunu yansıtmaz; aynı dönemde dolar
        // TL karşısında daha çok değer kazanmışsa TL bazında sonuç pozitif bile çıkabilir (proje
        // sohbeti: PSKY örneği — $ bazında -%74,6 ama TL karşılığı 122→507 ₺, yani ARTMIŞ). "%"
        // rozeti, sıralama (kazanan/kaybeden) VE TL sonucu (resultAmount) hep AYNI TL-bazlı
        // getiriden gelmeli — üçü birbiriyle çelişmesin diye. Parite verisi kaynaklarımızda
        // ~1989-11-07'den öncesine gitmiyor — bu tarihten önceki günler için "aynı gün ne alsaydın"
        // satırı üretmenin bir anlamı yok, TL karşılığı zaten hesaplanamıyor. BIST zaten native TL
        // olduğu için bu kısıt/dönüşüm uygulanmaz.
        var needsTlComposition = category is TimeMachineCategory.UsStocks or TimeMachineCategory.Crypto;
        DateTime[] usdTryDates = [];
        decimal[] usdTryCloses = [];
        decimal usdTryEnd = 0m;

        if (needsTlComposition)
        {
            var usdTry = await _uow.Stocks.GetBySymbolAsync("USDTRY", ct);
            if (usdTry?.EarliestDataDate is { } parityFloor && scanFrom < parityFloor.Date)
                scanFrom = parityFloor.Date;

            if (usdTry is not null)
            {
                var usdTryPrices = await _uow.PriceHistories.GetByStockIdAsync(usdTry.Id, ct: ct);
                var sorted = usdTryPrices.Where(p => p.Close > 0m).OrderBy(p => p.Date).ToList();
                usdTryDates = sorted.Select(p => p.Date.Date).ToArray();
                usdTryCloses = sorted.Select(p => p.Close).ToArray();
                var endIdx = OnOrBeforeIndex(usdTryDates, endDate);
                if (endIdx >= 0)
                    usdTryEnd = usdTryCloses[endIdx];
            }

            if (usdTryEnd <= 0m)
                return await EmptyAsync(category, sw, "USD/TRY parite verisi yok — TL karşılığı hesaplanamıyor.", ct);
        }

        var rows = new List<TimeMachineLeader>();
        var buffer = new TopBuffer(TopN);
        var lossBuffer = new TopBuffer(LossTopN, worst: true);
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
                lossBuffer.Reset();

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

                    var returnPct = (endRetPx - startRet) / startRet * 100m;

                    // Kaynak feed'in (Yahoo/TV) AdjustedClose'u bazı hisselerde (ör. ROK, HUBB —
                    // ne split ne spin-off kaydı var ama getiri "-%73" diyor) bozuk gelebiliyor.
                    // Düzeltilmiş getiri ile HAM fiyat hareketi taban tabana zıt yöndeyse (biri büyük
                    // kazanç biri büyük kayıp diyorsa) kaynağa güvenilmez — o günü hiçbir buffer'a
                    // teklif etme; "kaybettirenler"de görünüp kullanıcıyı yanıltmasın.
                    if (useAdjusted)
                    {
                        var rawReturnPct = (endRawPx - row.Close) / row.Close * 100m;
                        if (Math.Sign(returnPct) != Math.Sign(rawReturnPct) &&
                            Math.Abs(returnPct) > 20m && Math.Abs(rawReturnPct) > 20m)
                            continue;
                    }

                    // "%" rozeti, kazanan/kaybeden sıralaması VE TL sonucu (resultAmount) hep AYNI
                    // TL-bazlı getiriden gelsin diye dolar getirisini o günkü/bugünkü USD/TRY
                    // kuruyla TL'ye çeviriyoruz (bkz. yukarıdaki "needsTlComposition" notu).
                    if (needsTlComposition)
                    {
                        var usdTryIdx = OnOrBeforeIndex(usdTryDates, date);
                        if (usdTryIdx < 0)
                            continue;

                        var usdTryStart = usdTryCloses[usdTryIdx];
                        if (usdTryStart <= 0m)
                            continue;

                        var tlStart = startRet * usdTryStart;
                        var tlEnd = endRetPx * usdTryEnd;
                        returnPct = (tlEnd - tlStart) / tlStart * 100m;
                    }

                    // StartPrice/EndPrice gösterim içindir — ham Close DEĞİL, ReturnPct'nin
                    // hesaplandığı aynı (düzeltilmiş) fiyat kullanılır. Proje sohbeti: OSTIM gibi
                    // çok sayıda bedelli/bedelsiz yaşamış hisselerde ham fiyat düşerken düzeltilmiş
                    // getirinin pozitif olması "fiyat düştü ama para arttı" diye kafa karıştırıyordu
                    // — ikisi tutarlı tek bir fiyat setinden gelmeli.
                    var candidate = new Candidate(
                        row.StockId,
                        byId[row.StockId].Symbol,
                        startRet,
                        endRetPx,
                        returnPct);
                    buffer.Offer(candidate);
                    lossBuffer.Offer(candidate);
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

                // Kaybedenler negatif Rank ile aynı tabloda saklanır (-1 = en çok kaybettiren).
                // Mevcut "Aynı gün ne alsaydın" sorgusu Rank > 0 filtreliyor, bu satırları hiç görmez.
                for (var rank = 0; rank < lossBuffer.Count; rank++)
                {
                    var c = lossBuffer[rank];
                    var stock = byId[c.StockId];
                    rows.Add(new TimeMachineLeader
                    {
                        Category = category,
                        StartDate = date,
                        Rank = -(rank + 1),
                        StockId = c.StockId,
                        Symbol = stock.Symbol,
                        Name = stock.Name,
                        StartPrice = c.StartPrice,
                        EndPrice = c.EndPrice,
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

    /// <summary>Artan sıralı <paramref name="sortedDates"/> içinde <paramref name="target"/>'a eşit
    /// veya ondan önceki en son tarihin index'i (yoksa -1) — USD/TRY kurunu, her gün için ayrı
    /// sorgu atmadan, tek geçişte O(log n) arayabilmek için.</summary>
    private static int OnOrBeforeIndex(DateTime[] sortedDates, DateTime target)
    {
        var idx = Array.BinarySearch(sortedDates, target);
        if (idx >= 0) return idx;
        var insertionPoint = ~idx;
        return insertionPoint - 1;
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
    /// <paramref name="worst"/> false ise en yüksek getiriyi (kazananlar), true ise en düşük
    /// getiriyi (kaybedenler — "günün zenginlik testi" özelliği) tutar. Aynı gün-tarama
    /// döngüsünde ikinci bir buffer olarak paralel çalışır, ekstra veri taraması gerektirmez.
    /// </summary>
    private sealed class TopBuffer
    {
        private readonly Candidate[] _items;
        private readonly bool _worst;

        public TopBuffer(int capacity, bool worst = false)
        {
            _items = new Candidate[capacity];
            _worst = worst;
        }

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

        private bool IsBetter(in Candidate a, in Candidate b)
            => _worst
                ? a.ReturnPct < b.ReturnPct
                    || (a.ReturnPct == b.ReturnPct && string.CompareOrdinal(a.Symbol, b.Symbol) < 0)
                : a.ReturnPct > b.ReturnPct
                    || (a.ReturnPct == b.ReturnPct && string.CompareOrdinal(a.Symbol, b.Symbol) < 0);
    }
}
