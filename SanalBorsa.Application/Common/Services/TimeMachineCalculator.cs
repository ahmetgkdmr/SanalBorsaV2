using System.Globalization;
using SanalBorsa.Application.Common.Constants;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Enums;

namespace SanalBorsa.Application.Common.Services;

/// <summary>
/// Para hesabı artık olay-bazlı simülasyon (her split/temettü/bedelliyi tek tek uygulayıp lot/nakit
/// takip etmek) yerine <see cref="StockPriceHistory.AdjustedClose"/> (TradingView "dividends" —
/// split + temettü dahil toplam getiri) serisindeki orana dayanıyor:
/// bugünküDeğer = yatırılanTutar × (AdjustedClose_bugün / AdjustedClose_alımGünü).
/// Bu, hangi olayın "gerçek temettü" hangisinin "spin-off" olduğunu bizim ayırt etmemize gerek
/// bırakmıyor, split'i çifte saymayı imkansız kılıyor ve bedelli'nin (rüçhan) doğru ekonomik
/// etkisini (TERP) bizim yerimize TradingView'e bırakıyor (bkz. proje sohbeti — GE/Citigroup
/// spin-off'ları ve GARAN bedelli testleri bu yaklaşımı doğruladı).
/// Olaylar (split/bedelli/bedelsiz/temettü) hâlâ hikaye/lotEvents için gösterilir, ama artık parayı
/// etkilemezler — sadece "bu tarihte şu oldu, lot sayın böyle değişti" bilgisi verirler; bedelli/
/// bedelsiz için lot çarpanı, olayın bildirdiği (bazen hatalı) orandan değil, o günün HAM fiyatındaki
/// gerçek öncesi/sonrası değişiminden (ampirik) türetilir.
/// </summary>
public static class TimeMachineCalculator
{
    private static readonly CultureInfo TrCulture = new("tr-TR");

    /// <summary>2005 öncesi 1 işlem lotu = 1000 adet; fiyat serisi bugünkü lot biriminde.</summary>
    private const int OldSharesPerLot = 1000;

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

        if (normalizedMode == "lump")
        {
            return CalculateLump(
                symbol, normalizedMode, dateLabel, buyDate, buyPrice, adjustedBuy, wage,
                monthlyPoints, orderedActions, orderedPrices, market);
        }

        return CalculateDca(
            symbol, normalizedMode, dateLabel, buyDate, buyPrice, wagePercentage, amount, market,
            monthlyPoints, orderedActions, orderedPrices);
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
        MarketType market)
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

        var series = new List<SimulationPointDto>();
        var valueSeries = new List<decimal>();
        var lotSeries = new List<decimal>();

        foreach (var point in monthlyPoints)
        {
            var adjAtPoint = point.AdjustedClose > 0m ? point.AdjustedClose : point.Price;
            var value = adjustedBuy > 0m ? invested * (adjAtPoint / adjustedBuy) : invested;
            series.Add(new SimulationPointDto(point.Year, point.Month, point.Price));
            valueSeries.Add(value);
            lotSeries.Add(point.Price > 0m ? RoundLots(value / point.Price) : 0m);
        }

        var currentValue = valueSeries[^1];
        var finalLots = lotSeries[^1];
        var gainPct = invested > 0 ? (currentValue - invested) / invested * 100m : 0m;

        var (lotEvents, dividendsReceived) = BuildNarrativeEvents(
            actions, dailyPrices, initialLots, buyDate);

        var story = BuildStoryLines(
            symbol, dateLabel, mode, invested, initialLots, finalLots, buyDate, market,
            dividendsReceived, currentValue, lotEvents);

        return new TimeMachineResultDto(
            symbol, mode, invested, currentValue, gainPct, initialLots, finalLots,
            buyPrice, monthlyPoints[^1].Price, series, valueSeries, lotSeries, lotEvents, dateLabel,
            dividendsReceived, 0m, 0m, 0m, story,
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
        IReadOnlyList<StockPriceHistory> dailyPrices)
    {
        // Her aylık katkı kendi alım anındaki AdjustedClose'una göre ayrı ayrı büyür; bir noktadaki
        // toplam değer, o ana kadarki bütün katkıların o günkü karşılıklarının toplamıdır.
        var contributions = new List<(decimal Amount, decimal AdjustedAtBuy)>();
        decimal invested = 0m;
        decimal initialLots = 0m;

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
            lotSeries.Add(point.Price > 0m ? RoundLots(value / point.Price) : 0m);
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

        var (lotEvents, dividendsReceived) = BuildNarrativeEvents(
            actions, dailyPrices, initialLots, buyDate);

        var story = BuildStoryLines(
            symbol, dateLabel, mode, invested, initialLots, finalLots, buyDate, market,
            dividendsReceived, currentValue, lotEvents);

        var firstAdjusted = contributions.Count > 0 ? contributions[0].AdjustedAtBuy : 0m;
        return new TimeMachineResultDto(
            symbol, mode, invested, currentValue, gainPct, initialLots, finalLots,
            buyPrice, monthlyPoints[^1].Price, series, valueSeries, lotSeries, lotEvents, dateLabel,
            dividendsReceived, 0m, 0m, 0m, story,
            DailySeries: BuildDcaDailySeries(dailyPrices, buyDate, contributions));
    }

    /// <summary>
    /// Olayları PARAYI etkilemeden, sadece hikaye/lotEvents için yürür: bedelsiz/bedelli lot
    /// çarpanı, olayın bildirdiği (bazen hatalı — bkz. rüçhan fiyatı 1000 sabit bug'ı) değerden
    /// değil, o günün ham kapanışındaki gerçek öncesi/sonrası değişiminden türetilir. Temettü
    /// sadece "bu kadar ödedi" diye raporlanır, yeniden yatırım/lot artışı iddia edilmez.
    /// </summary>
    private static (List<LotEventMarkerDto> Events, decimal DividendsReceived) BuildNarrativeEvents(
        IReadOnlyList<CorporateAction> actions,
        IReadOnlyList<StockPriceHistory> dailyPrices,
        decimal initialLots,
        DateTime buyDate)
    {
        var events = new List<LotEventMarkerDto>();
        var narrativeLots = initialLots;
        var dividendsReceived = 0m;

        foreach (var action in actions)
        {
            var lotsBefore = RoundLots(narrativeLots);
            decimal? cashReceived = null;
            string? story = null;
            var pointYear = action.ActionDate.Year;
            var pointMonth = action.ActionDate.Month;

            switch (action.ActionType)
            {
                case CorporateActionType.Dividend:
                {
                    var received = narrativeLots * action.Value;
                    if (received <= 0m) continue;
                    dividendsReceived += received;
                    cashReceived = received;
                    story = $"{action.ActionDate:d MMMM yyyy}: {received:N2} ₺ temettü verdi.";
                    break;
                }

                case CorporateActionType.BonusIssue:
                case CorporateActionType.RightsIssue:
                {
                    var multiplier = EmpiricalLotMultiplier(dailyPrices, action.ActionDate);
                    if (multiplier is null || multiplier.Value <= 0m || Math.Abs(multiplier.Value - 1m) < 0.001m)
                        continue;

                    narrativeLots = RoundLots(narrativeLots * multiplier.Value);
                    var isBedelli = action.ActionType == CorporateActionType.RightsIssue;
                    var label = isBedelli ? "Bedelli" : multiplier.Value < 1m ? "Ters split" : "Bedelsiz";
                    story = multiplier.Value < 1m
                        ? $"{action.ActionDate:d MMMM yyyy}: {label} (÷{1m / multiplier.Value:0.##}) → lot {FormatLots(lotsBefore)} → {FormatLots(narrativeLots)}"
                        : $"{action.ActionDate:d MMMM yyyy}: {label} (×{multiplier.Value:0.##}) → lot {FormatLots(lotsBefore)} → {FormatLots(narrativeLots)}";
                    break;
                }

                default:
                    continue;
            }

            events.Add(new LotEventMarkerDto(
                pointYear, pointMonth,
                action.ActionDate.ToString("d MMMM yyyy", TrCulture),
                action.ActionType.ToString(),
                BuildLotEventLabel(action),
                lotsBefore, RoundLots(narrativeLots), action.Description,
                cashReceived, null, story, action.ActionDate.Day));
        }

        return (events, dividendsReceived);
    }

    /// <summary>
    /// Bir olayın gerçek lot çarpanını, olayın kendi bildirdiği orandan değil, o günün ham
    /// kapanışındaki önceki/sonraki günün gerçek fiyat değişiminden çıkarır (çarpan = önceki/sonraki
    /// fiyat oranı). Bedelsizde bu zaten olayın value'suyla örtüşür (bedava, saf matematik); bedelli
    /// gibi bedel içeren olaylarda ise gerçek (TERP'e uygun) ekonomik etkiyi yansıtır — rüçhan
    /// fiyatı verisine hiç ihtiyaç duymadan.
    /// </summary>
    private static decimal? EmpiricalLotMultiplier(IReadOnlyList<StockPriceHistory> dailyPrices, DateTime actionDate)
    {
        StockPriceHistory? before = null;
        foreach (var p in dailyPrices)
        {
            if (p.Date.Date >= actionDate.Date) break;
            before = p;
        }

        StockPriceHistory? after = null;
        foreach (var p in dailyPrices)
        {
            if (p.Date.Date < actionDate.Date) continue;
            after = p;
            break;
        }

        if (before is null || after is null || before.Close <= 0m || after.Close <= 0m)
            return null;

        return before.Close / after.Close;
    }

    private static List<string> BuildStoryLines(
        string symbol,
        string dateLabel,
        string mode,
        decimal invested,
        decimal initialLots,
        decimal finalLots,
        DateTime buyDate,
        MarketType market,
        decimal dividendsReceived,
        decimal currentValue,
        IReadOnlyList<LotEventMarkerDto> events)
    {
        var lines = new List<string>();

        if (mode == "dca")
        {
            lines.Add(
                $"{dateLabel}'den bugüne düzenli alımla toplam {FormatMoney(invested)} ₺ yatırdın; ilk birikimin ~{FormatLots(initialLots)} lot {symbol}.");
        }
        else if (market == MarketType.Bist && buyDate.Year < 2005)
        {
            var adet = (long)Math.Round(initialLots * OldSharesPerLot);
            lines.Add(
                $"{dateLabel}'de {FormatMoney(invested)} ₺ ile {adet:N0} adet {symbol} aldın " +
                $"(o dönemde 1000 adet = 1 lot → ≈ {FormatLots(initialLots)} lot; 2005’te bu birime geçildi).");
        }
        else
        {
            lines.Add(
                $"{dateLabel}'de {FormatMoney(invested)} ₺ ile {FormatLots(initialLots)} lot {symbol} aldın.");
        }

        var bonusOrRights = events.Count(e =>
            e.ActionType is nameof(CorporateActionType.BonusIssue) or nameof(CorporateActionType.RightsIssue));
        if (bonusOrRights > 0)
        {
            var bedelsiz = events.Count(e => e.ActionType == nameof(CorporateActionType.BonusIssue));
            var bedelli = events.Count(e => e.ActionType == nameof(CorporateActionType.RightsIssue));
            var parts = new List<string>();
            if (bedelsiz > 0) parts.Add($"{bedelsiz} bedelsiz");
            if (bedelli > 0) parts.Add($"{bedelli} bedelli");
            var lastLots = events[^1].LotsAfter;
            lines.Add(
                $"{string.Join(" ve ", parts)} oldu — lot sayın {FormatLots(initialLots)} → {FormatLots(lastLots)} arasında değişti.");
        }

        if (dividendsReceived > 0m)
        {
            lines.Add($"Bu süreçte toplam {FormatMoney(dividendsReceived)} ₺ temettü verdi.");
        }

        lines.Add(
            $"Sonuç: bugünkü karşılığı ~{FormatLots(finalLots)} lot · portföy değeri {FormatMoney(currentValue)} ₺.");

        return lines;
    }

    private static string BuildLotEventLabel(CorporateAction action)
        => action.ActionType switch
        {
            CorporateActionType.BonusIssue when action.Value < 1m =>
                $"Ters split (÷{(1m / action.Value):0.##})",
            CorporateActionType.BonusIssue =>
                $"Bedelsiz %{((action.Value - 1m) * 100m):0.#} (×{action.Value:0.##})",
            CorporateActionType.RightsIssue =>
                $"Bedelli %{(action.Value * 100m):0.#}",
            CorporateActionType.Dividend =>
                $"Temettü {action.Value:0.####} ₺/lot",
            _ => "Şirket olayı",
        };

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

        while (cursor <= end)
        {
            var monthEnd = new DateTime(cursor.Year, cursor.Month, DateTime.DaysInMonth(cursor.Year, cursor.Month));
            if (monthEnd > latestDate)
                monthEnd = latestDate;

            var monthPrice = prices
                .Where(p => p.Date.Year == cursor.Year && p.Date.Month == cursor.Month)
                .OrderByDescending(p => p.Date)
                .FirstOrDefault();

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
