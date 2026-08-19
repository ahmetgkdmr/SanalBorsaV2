using System.Globalization;
using SanalBorsa.Application.Common.Constants;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Enums;

namespace SanalBorsa.Application.Common.Services;

/// <summary>
/// Para hesabı <see cref="StockPriceHistory.AdjustedClose"/> (TradingView "dividends" — split +
/// temettü dahil toplam getiri) serisindeki orana dayanıyor:
/// bugünküDeğer = yatırılanTutar × (AdjustedClose_bugün / AdjustedClose_alımGünü).
/// Bu, hangi olayın "gerçek temettü" hangisinin "spin-off" olduğunu bizim ayırt etmemize gerek
/// bırakmıyor, split'i çifte saymayı imkansız kılıyor ve bedelli'nin (rüçhan) doğru ekonomik
/// etkisini (TERP) bizim yerimize TradingView'e bırakıyor.
///
/// GEÇMİŞ HAM FİYATA (ve ondan türetilen "o gün kaç lot aldın / şu bedelsizle lotun kaça çıktı" gibi
/// olay-bazlı lot zincirlemesine) ARTIK HİÇ GÜVENMİYORUZ — proje sohbeti: AFYON 2010 vakasında
/// (KAP kararı ile fiili hak kullanımı arasında aylarca süren, hissenin "eski/yeni" iki ayrı tahtada
/// paralel işlem gördüğü bir dönem) ham fiyat serisinin kendisi tutarsız çıktı; kayıtlı kurumsal
/// olayların büyüklüğü de (İş Yatırım kaynaklı "1 TL" temettü gibi) hatalı olabiliyor. Bu yüzden:
/// - "Lot" sayısı artık SADECE bugünün (her zaman güvenilir) ham fiyatına bölünerek türetiliyor
///   (bugünküLot = bugünküDeğer / bugünküHamFiyat) — para eğrisiyle aynı şekle sahip, düz bir eğri;
///   hiçbir ay/olayda ham fiyat gürültüsünden kaynaklı sahte bir sıçrama YAPAMAZ.
/// - Kurumsal olaylar (lotEvents) artık lot/nakit değişimi İDDİA ETMİYOR — sadece "bu tarihte bu
///   TÜRDE bir olay oldu" bilgisini taşıyor (bkz. BuildEventMarkers).
/// </summary>
public static class TimeMachineCalculator
{
    private static readonly CultureInfo TrCulture = new("tr-TR");

    /// <summary>2005 öncesi Yeni TL rakamları, gerçek (eski) nominal tutarı 1.000.000'a bölerek
    /// saklanıyor (bkz. MinimumWageByYear) — hikayede önce gerçek eski TL rakamı, yanında parantez
    /// içinde Yeni TL karşılığı gösteriliyor.</summary>
    private static readonly DateTime RedenominationDate = new(2005, 1, 1);

    /// <summary>En küçük alınabilir miktar (≈ 1 eski adet).</summary>
    private const decimal MinLots = 0.001m;

    public static TimeMachineResultDto Calculate(
        string symbol,
        IReadOnlyList<StockPriceHistory> prices,
        IReadOnlyList<CorporateAction> actions,
        DateTime buyDate,
        decimal wagePercentage,
        string mode,
        decimal? amount = null,
        MarketType market = MarketType.Bist)
    {
        // Türkiye asgari ücreti sadece BIST için anlamlı bir varsayılan yatırım tutarı çıpası —
        // ABD (veya başka bir piyasa) için sahte bir "asgari ücret" tablosu uydurmak yerine
        // amount (USD) zorunlu tutulur.
        if (market != MarketType.Bist && (amount is null || amount.Value <= 0))
        {
            return Error(symbol, mode, buyDate, "Bu piyasa için yatırım tutarı (amount) zorunludur.");
        }

        if (prices.Count == 0)
            return Error(symbol, mode, buyDate, "Bu hisse için fiyat geçmişi bulunamadı.");

        var orderedPrices = prices.OrderBy(p => p.Date).ToList();
        var orderedActions = actions
            .Where(a => a.ActionDate.Date >= buyDate.Date)
            .OrderBy(a => a.ActionDate)
            .ToList();
        var earliest = orderedPrices[0].Date.Date;

        if (buyDate.Date < earliest)
        {
            return Error(
                symbol,
                mode,
                buyDate,
                $"Veri {earliest:dd.MM.yyyy} tarihinden başlıyor. Daha eski bir tarih seç.");
        }

        var buyEntry = FindOnOrAfter(orderedPrices, buyDate);
        if (buyEntry is null)
            return Error(symbol, mode, buyDate, "Seçilen tarihte işlem günü bulunamadı.");

        var buyPrice = buyEntry.Close;
        var adjustedBuy = buyEntry.AdjustedClose > 0m ? buyEntry.AdjustedClose : buyPrice;
        var wage = amount.HasValue && amount.Value > 0
            ? amount.Value
            : MinimumWageByYear.Get(buyDate) * wagePercentage / 100m;
        var dateLabel = buyDate.ToString("d MMMM yyyy", TrCulture);
        var normalizedMode = mode.Equals("dca", StringComparison.OrdinalIgnoreCase) ? "dca" : "lump";

        var monthlyPoints = BuildMonthlyPricePoints(orderedPrices, buyDate.Date);
        if (monthlyPoints.Count == 0)
            return Error(symbol, normalizedMode, buyDate, "Simülasyon için yeterli fiyat verisi yok.");

        var isWageBased = !(amount.HasValue && amount.Value > 0);

        if (normalizedMode == "lump")
        {
            return CalculateLump(
                symbol, normalizedMode, dateLabel, buyDate, buyPrice, adjustedBuy, wage,
                monthlyPoints, orderedActions, orderedPrices, market, isWageBased);
        }

        return CalculateDca(
            symbol, normalizedMode, dateLabel, buyDate, buyPrice, wagePercentage, amount, market,
            monthlyPoints, orderedActions, orderedPrices, isWageBased);
    }

    private static TimeMachineResultDto CalculateLump(
        string symbol,
        string mode,
        string dateLabel,
        DateTime buyDate,
        decimal buyPrice,
        decimal adjustedBuy,
        decimal wage,
        IReadOnlyList<MonthlyPricePoint> monthlyPoints,
        IReadOnlyList<CorporateAction> actions,
        IReadOnlyList<StockPriceHistory> dailyPrices,
        MarketType market,
        bool isWageBased)
    {
        var initialLots = buyPrice > 0m ? RoundLots(wage / buyPrice) : 0m;
        if (initialLots < MinLots)
        {
            return new TimeMachineResultDto(
                symbol, mode, 0, 0, 0, 0, 0, buyPrice, monthlyPoints[^1].Price,
                [], [], [], [], dateLabel,
                Error: $"{dateLabel} günü {FormatMoney(wage)} ₺ ile {symbol} alınamıyor (fiyat ~{FormatMoney(buyPrice)} ₺). Tutarı artır.");
        }

        var invested = wage;
        var todayPrice = monthlyPoints[^1].Price;

        var series = new List<SimulationPointDto>();
        var valueSeries = new List<decimal>();
        var lotSeries = new List<decimal>();

        foreach (var point in monthlyPoints)
        {
            var adjAtPoint = point.AdjustedClose > 0m ? point.AdjustedClose : point.Price;
            var value = adjustedBuy > 0m ? invested * (adjAtPoint / adjustedBuy) : invested;
            series.Add(new SimulationPointDto(point.Year, point.Month, point.Price));
            valueSeries.Add(value);
            // Lot eğrisi SADECE bugünün (sabit, güvenilir) ham fiyatına bölünüyor — bkz. dosya başı
            // açıklaması: geçmişin ham fiyatına asla güvenmiyoruz, bu yüzden lot eğrisi para eğrisiyle
            // aynı şekle sahip, düz bir eğri (hiçbir ay/olayda sahte sıçrama yapamaz).
            lotSeries.Add(todayPrice > 0m ? RoundLots(value / todayPrice) : 0m);
        }

        var currentValue = valueSeries[^1];
        var finalLots = lotSeries[^1];
        var gainPct = invested > 0 ? (currentValue - invested) / invested * 100m : 0m;

        // "Başlangıç" lot da bugünün fiyatına bölünüyor (finalLots ile aynı payda) — böylece
        // başlangıç→bugün farkı SADECE para büyümesini yansıtır, ham fiyat kaynaklı sahte bir
        // sıçrama görünmez (bkz. dosya başı açıklaması).
        var initialLotsSafe = todayPrice > 0m ? RoundLots(invested / todayPrice) : 0m;

        var lotEvents = BuildEventMarkers(actions, invested, adjustedBuy, todayPrice, dailyPrices);

        var story = BuildStoryLines(
            dateLabel, mode, invested, finalLots, buyDate, market,
            currentValue, lotEvents, isWageBased);

        return new TimeMachineResultDto(
            // BuyPrice: o günün ham kapanışı değil, "bugünün alım gücüyle" karşılığı (adjustedBuy) —
            // bkz. dosya başı açıklaması, geçmişin ham fiyatını hiçbir yerde göstermiyoruz artık.
            symbol, mode, invested, currentValue, gainPct, initialLotsSafe, finalLots,
            adjustedBuy, todayPrice, series, valueSeries, lotSeries, lotEvents, dateLabel,
            0m, 0m, 0m, 0m, story,
            DailySeries: BuildDailySeries(dailyPrices, buyDate, invested, adjustedBuy));
    }

    private static TimeMachineResultDto CalculateDca(
        string symbol,
        string mode,
        string dateLabel,
        DateTime buyDate,
        decimal buyPrice,
        decimal wagePercentage,
        decimal? amount,
        MarketType market,
        IReadOnlyList<MonthlyPricePoint> monthlyPoints,
        IReadOnlyList<CorporateAction> actions,
        IReadOnlyList<StockPriceHistory> dailyPrices,
        bool isWageBased)
    {
        // Her aylık katkı kendi alım anındaki AdjustedClose'una göre ayrı ayrı büyür; bir noktadaki
        // toplam değer, o ana kadarki bütün katkıların o günkü karşılıklarının toplamıdır.
        var contributions = new List<(decimal Amount, decimal AdjustedAtBuy)>();
        decimal invested = 0m;
        decimal initialLots = 0m;
        var todayPrice = monthlyPoints[^1].Price;

        var series = new List<SimulationPointDto>();
        var valueSeries = new List<decimal>();
        var lotSeries = new List<decimal>();

        for (var i = 0; i < monthlyPoints.Count; i++)
        {
            var point = monthlyPoints[i];
            var adjAtPoint = point.AdjustedClose > 0m ? point.AdjustedClose : point.Price;

            if (i < monthlyPoints.Count - 1)
            {
                var monthlyWage = amount.HasValue && amount.Value > 0
                    ? amount.Value
                    : MinimumWageByYear.Get(point.MonthEnd) * wagePercentage / 100m;

                if (monthlyWage > 0m && adjAtPoint > 0m)
                {
                    contributions.Add((monthlyWage, adjAtPoint));
                    invested += monthlyWage;

                    if (initialLots == 0m && point.Price > 0m)
                        initialLots = RoundLots(monthlyWage / point.Price);
                }
            }

            var value = contributions.Sum(c => c.AdjustedAtBuy > 0m ? c.Amount * (adjAtPoint / c.AdjustedAtBuy) : 0m);
            series.Add(new SimulationPointDto(point.Year, point.Month, point.Price));
            valueSeries.Add(value);
            // Lot eğrisi burada da sabit (bugünkü) fiyata bölünüyor — bkz. CalculateLump açıklaması.
            lotSeries.Add(todayPrice > 0m ? RoundLots(value / todayPrice) : 0m);
        }

        var finalLots = lotSeries.Count > 0 ? lotSeries[^1] : 0m;
        if (finalLots < MinLots)
        {
            return new TimeMachineResultDto(
                symbol, mode, 0, 0, 0, 0, 0, buyPrice, monthlyPoints[^1].Price,
                [], [], [], [], dateLabel,
                Error: "Bu oranla birikim hisse almaya yetmemiş. Oranı artırmayı dene.");
        }

        if (initialLots == 0m)
            initialLots = finalLots;

        var currentValue = valueSeries[^1];
        var gainPct = invested > 0 ? (currentValue - invested) / invested * 100m : 0m;
        var firstAdjusted = contributions.Count > 0 ? contributions[0].AdjustedAtBuy : 0m;
        var firstContribution = contributions.Count > 0 ? contributions[0].Amount : 0m;

        // Başlangıç lotu da bugünün fiyatına bölünüyor — bkz. CalculateLump açıklaması.
        var initialLotsSafe = todayPrice > 0m && firstContribution > 0m
            ? RoundLots(firstContribution / todayPrice)
            : finalLots;

        var lotEvents = BuildEventMarkers(actions, invested, firstAdjusted, todayPrice, dailyPrices);

        var story = BuildStoryLines(
            dateLabel, mode, invested, finalLots, buyDate, market,
            currentValue, lotEvents, isWageBased);

        return new TimeMachineResultDto(
            // BuyPrice burada da adjustedBuy'ın DCA karşılığı (ilk katkının o günkü, "bugünün alım
            // gücüyle" değeri) — ham fiyat değil.
            symbol, mode, invested, currentValue, gainPct, initialLotsSafe, finalLots,
            firstAdjusted, todayPrice, series, valueSeries, lotSeries, lotEvents, dateLabel,
            0m, 0m, 0m, 0m, story,
            DailySeries: BuildDcaDailySeries(dailyPrices, buyDate, contributions));
    }

    /// <summary>
    /// Kurumsal olayları artık lot/nakit DEĞİŞİMİ iddia etmeden, sadece "bu tarihte bu TÜRDE bir
    /// olay oldu" bilgisi olarak işaretler — proje sohbeti: geçmişin ham fiyatından (ya da olayın
    /// kendi bildirdiği, bazen hatalı yüzdeden — bkz. AFYON'un uydurma ×25 kaydı, AFYON'un şişirilmiş
    /// "1 TL" temettüleri) türetilen "önce/sonra lot" hesabı defalarca yanlış çıktı; bazı vakalarda
    /// (AFYON 2010) ham fiyat serisinin kendisi bile tutarsızdı (aylarca süren eski/yeni tahta
    /// ayrımı). Her işaretteki LotsBefore/LotsAfter, o tarihteki NOTİONAL lotu (o günkü para değeri
    /// ÷ BUGÜNKÜ sabit fiyat) taşır — ikisi eşit, yani hiçbir sıçrama iddia edilmiyor, sadece "para
    /// eğrisinde bu noktada bir olay var" bilgisi.
    /// </summary>
    private static List<LotEventMarkerDto> BuildEventMarkers(
        IReadOnlyList<CorporateAction> actions,
        decimal invested,
        decimal adjustedBuy,
        decimal todayPrice,
        IReadOnlyList<StockPriceHistory> dailyPrices)
    {
        var events = new List<LotEventMarkerDto>();

        foreach (var action in actions)
        {
            if (action.ActionType is not (CorporateActionType.BonusIssue
                or CorporateActionType.RightsIssue or CorporateActionType.Dividend))
                continue;

            var adjAtAction = FindAdjustedCloseOnOrAfter(dailyPrices, action.ActionDate) ?? adjustedBuy;
            var valueAtAction = adjustedBuy > 0m ? invested * (adjAtAction / adjustedBuy) : invested;
            var notionalLots = todayPrice > 0m ? RoundLots(valueAtAction / todayPrice) : 0m;

            var (label, storyVerb) = action.ActionType switch
            {
                CorporateActionType.BonusIssue => ("Bedelsiz", "bedelsiz sermaye artırımı oldu"),
                CorporateActionType.RightsIssue => ("Bedelli", "bedelli sermaye artırımı oldu"),
                CorporateActionType.Dividend => ("Temettü", "temettü ödendi"),
                _ => ("Şirket olayı", "bir şirket olayı oldu"),
            };

            events.Add(new LotEventMarkerDto(
                action.ActionDate.Year, action.ActionDate.Month,
                action.ActionDate.ToString("d MMMM yyyy", TrCulture),
                action.ActionType.ToString(),
                label, notionalLots, notionalLots, action.Description,
                null, null,
                $"{action.ActionDate:d MMMM yyyy}: {storyVerb}.",
                action.ActionDate.Day));
        }

        return events;
    }

    private static decimal? FindAdjustedCloseOnOrAfter(
        IReadOnlyList<StockPriceHistory> dailyPrices, DateTime date)
    {
        foreach (var p in dailyPrices)
        {
            if (p.Date.Date < date.Date) continue;
            return p.AdjustedClose > 0m ? p.AdjustedClose : null;
        }
        return null;
    }

    /// <summary>
    /// Hikaye iki satıra indirildi — proje sohbeti: "o gün hisse bugünün parasıyla X ₺'ydi" bilgisi
    /// zaten sonuç ekranındaki ayrı bir kutuda (BuyPrice) gösteriliyor, burada tekrar etmiyor;
    /// asgari-ücret-bazlı yatırımlarda o dönemin asgari ücret bağlamı, ayrı bir satır yerine doğrudan
    /// yatırım cümlesine gömülüyor. Kurumsal olaylar yıl yıl değil, TÜRE göre toplam sayı olarak
    /// özetleniyor — büyüklük/lot değişimi iddia edilmiyor (bkz. dosya başı açıklaması).
    /// </summary>
    private static List<string> BuildStoryLines(
        string dateLabel,
        string mode,
        decimal invested,
        decimal finalLots,
        DateTime buyDate,
        MarketType market,
        decimal currentValue,
        IReadOnlyList<LotEventMarkerDto> events,
        bool isWageBased)
    {
        var lines = new List<string>();

        // "invested", DCA'da TÜM ayların TOPLAMI — "her ay ... yatırsaydın" cümlesinin nesnesi
        // olarak doğrudan yazılırsa, o dev toplam sanki AYLIK tutarmış gibi okunuyordu (proje
        // sohbeti: 37 yıllık bir DCA'da "her ay bir asgari ücret olan 1 trilyon TL" gibi saçma bir
        // cümle çıkıyordu). Bu yüzden DCA'da toplam ayrı, "(toplamda X yatırmış olurdun)" şeklinde
        // parantez içinde; aylık tutarın kendisi (asgari ücret bazlıysa yıldan yıla değiştiği için)
        // hiç sayı olarak iddia edilmiyor.
        var investedLabel = FormatOldTlAware(invested, buyDate);
        var wageContext = isWageBased && market == MarketType.Bist
            ? "bir asgari ücret olan "
            : "";

        // Bugünkü TL değeri (currentValue) burada bilerek yok — frontend onu ayrı, kazanç/kayıp
        // rengiyle (yeşil/kırmızı) boyanmış bir cümle olarak ayrıca gösteriyor.
        lines.Add(mode == "dca"
            ? (isWageBased && market == MarketType.Bist
                ? $"{dateLabel}'den bugüne her ay bir asgari ücret yatırsaydın " +
                  $"(toplamda {investedLabel} yatırmış olurdun), bugün toplam ~{FormatLots(finalLots)} lotun olurdu."
                : $"{dateLabel}'den bugüne düzenli olarak her ay yatırsaydın " +
                  $"(toplamda {investedLabel} yatırmış olurdun), bugün toplam ~{FormatLots(finalLots)} lotun olurdu.")
            : $"{dateLabel}'de {wageContext}{investedLabel} yatırsaydın, bugün toplam ~{FormatLots(finalLots)} lotun olurdu.");

        var eventCounts = events
            .GroupBy(e => e.Label)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{g.Count()} {g.Key.ToLowerInvariant()}")
            .ToList();
        if (eventCounts.Count > 0)
        {
            lines.Add("Günümüze kadar " + string.Join(", ", eventCounts) + " oldu.");
        }

        return lines;
    }

    /// <summary>
    /// 2005 öncesi tarihler için, Yeni TL cinsinden saklanan tutarı gerçek (eski) nominal karşılığına
    /// (×1.000.000) çevirip önce onu, yanında parantez içinde Yeni TL karşılığını gösterir — proje
    /// sohbeti: "1993 asgari ücreti 1,56 ₺'ydi" demek yanıltıcı, o dönem gerçekte "1.563.473 TL"
    /// yazıyordu, 1,56 ₺ sadece bugünkü fiyat serisiyle aynı birimde göstermek için sonradan
    /// bölünmüş bir rakam.
    /// </summary>
    private static string FormatOldTlAware(decimal newTlAmount, DateTime date)
    {
        if (date >= RedenominationDate)
            return $"{FormatMoney(newTlAmount)} ₺";

        var oldTlAmount = newTlAmount * 1_000_000m;
        return $"{FormatMoney(oldTlAmount)} TL ({FormatMoney(newTlAmount)} ₺ Yeni TL karşılığı)";
    }

    private static StockPriceHistory? FindOnOrAfter(
        IReadOnlyList<StockPriceHistory> prices,
        DateTime date)
        => prices.FirstOrDefault(p => p.Date.Date >= date.Date);

    private static decimal RoundLots(decimal lots)
        => Math.Round(lots, 6, MidpointRounding.AwayFromZero);

    private static string FormatLots(decimal lots)
        => lots >= 100m
            ? lots.ToString("N0", TrCulture)
            : lots >= 10m
                ? lots.ToString("N1", TrCulture)
                : lots.ToString("N2", TrCulture);

    /// <summary>Birim fiyat — düşük fiyatlı hisselerde (ör. 0,40 ₺) anlamlı kalsın diye 4 basamağa
    /// kadar ondalık gösterir ama gereksiz sondaki sıfırları (0,4000 değil 0,4) atar.</summary>
    private static string FormatPrice(decimal price)
        => Math.Abs(price) >= 100m
            ? price.ToString("N0", TrCulture)
            : price.ToString("#,##0.####", TrCulture);

    private static string FormatMoney(decimal value)
        => value >= 100m
            ? value.ToString("N0", TrCulture)
            : value.ToString("N2", TrCulture);

    private static List<MonthlyPricePoint> BuildMonthlyPricePoints(
        IReadOnlyList<StockPriceHistory> prices,
        DateTime fromDate)
    {
        var latestDate = prices[^1].Date.Date;
        var cursor = new DateTime(fromDate.Year, fromDate.Month, 1);
        var end = new DateTime(latestDate.Year, latestDate.Month, 1);
        var points = new List<MonthlyPricePoint>();

        // Ay başına en güncel fiyatı O(N) tek geçişte topla — eskiden her ay için tüm fiyat
        // listesi baştan taranıyordu (O(ay × N)); 10 yıllık günlük seride (~2500 satır, ~120 ay)
        // bu ~300 bin karşılaştırmaya çıkıyordu. `prices` zaten tarihe göre artan sıralı geldiği
        // için tek geçişte üzerine yazarak her ay için en güncel barı doğrudan elde ediyoruz —
        // davranış birebir aynı (OrderByDescending().FirstOrDefault() ile eşdeğer), sadece O(N).
        var byMonth = new Dictionary<(int Year, int Month), StockPriceHistory>();
        foreach (var p in prices)
            byMonth[(p.Date.Year, p.Date.Month)] = p;

        while (cursor <= end)
        {
            var monthEnd = new DateTime(cursor.Year, cursor.Month, DateTime.DaysInMonth(cursor.Year, cursor.Month));
            if (monthEnd > latestDate)
                monthEnd = latestDate;

            byMonth.TryGetValue((cursor.Year, cursor.Month), out var monthPrice);

            if (monthPrice is not null)
                points.Add(new MonthlyPricePoint(
                    cursor.Year, cursor.Month, monthEnd, monthPrice.Close, monthPrice.AdjustedClose));

            cursor = cursor.AddMonths(1);
        }

        return points;
    }

    private static DailySeriesDto? BuildDailySeries(
        IReadOnlyList<StockPriceHistory> dailyPrices,
        DateTime buyDate,
        decimal invested,
        decimal adjustedBuy)
    {
        var fromIdx = dailyPrices
            .Select((p, i) => (p, i))
            .FirstOrDefault(t => t.p.Date.Date >= buyDate.Date, (null!, -1));
        if (fromIdx.p is null)
            return null;

        var days = new List<int>();
        var prices = new List<decimal>();
        var values = new List<decimal>();
        var start = fromIdx.p.Date.Date;

        for (var i = fromIdx.i; i < dailyPrices.Count; i++)
        {
            var p = dailyPrices[i];
            var adj = p.AdjustedClose > 0m ? p.AdjustedClose : p.Close;
            var value = adjustedBuy > 0m ? invested * (adj / adjustedBuy) : invested;
            days.Add((int)(p.Date.Date - start).TotalDays);
            prices.Add(Math.Round(p.Close, 4));
            values.Add(Math.Round(value, 2));
        }

        return days.Count < 2
            ? null
            : new DailySeriesDto(start.ToString("yyyy-MM-dd"), days, prices, values);
    }

    private static DailySeriesDto? BuildDcaDailySeries(
        IReadOnlyList<StockPriceHistory> dailyPrices,
        DateTime buyDate,
        IReadOnlyList<(decimal Amount, decimal AdjustedAtBuy)> contributions)
    {
        if (contributions.Count == 0)
            return null;

        var fromIdx = dailyPrices
            .Select((p, i) => (p, i))
            .FirstOrDefault(t => t.p.Date.Date >= buyDate.Date, (null!, -1));
        if (fromIdx.p is null)
            return null;

        var days = new List<int>();
        var prices = new List<decimal>();
        var values = new List<decimal>();
        var start = fromIdx.p.Date.Date;

        for (var i = fromIdx.i; i < dailyPrices.Count; i++)
        {
            var p = dailyPrices[i];
            var adj = p.AdjustedClose > 0m ? p.AdjustedClose : p.Close;
            var value = contributions.Sum(c => c.AdjustedAtBuy > 0m ? c.Amount * (adj / c.AdjustedAtBuy) : 0m);
            days.Add((int)(p.Date.Date - start).TotalDays);
            prices.Add(Math.Round(p.Close, 4));
            values.Add(Math.Round(value, 2));
        }

        return days.Count < 2
            ? null
            : new DailySeriesDto(start.ToString("yyyy-MM-dd"), days, prices, values);
    }

    private static TimeMachineResultDto Error(
        string symbol,
        string mode,
        DateTime buyDate,
        string message)
        => new(
            symbol,
            mode,
            0, 0, 0, 0, 0, 0, 0,
            [], [], [], [],
            buyDate.ToString("d MMMM yyyy", TrCulture),
            Error: message);

    private sealed record MonthlyPricePoint(
        int Year, int Month, DateTime MonthEnd, decimal Price, decimal AdjustedClose);
}
