using System.Globalization;
using SanalBorsa.Application.Common.Constants;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Enums;

namespace SanalBorsa.Application.Common.Services;

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
        decimal? amount = null)
    {
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
                symbol, normalizedMode, dateLabel, buyDate, buyPrice, wage,
                monthlyPoints, orderedActions, orderedPrices);
        }

        return CalculateDca(
            symbol, normalizedMode, dateLabel, buyDate, buyPrice, wagePercentage, amount,
            monthlyPoints, orderedActions, orderedPrices);
    }

    private static TimeMachineResultDto CalculateLump(
        string symbol,
        string mode,
        string dateLabel,
        DateTime buyDate,
        decimal buyPrice,
        decimal wage,
        IReadOnlyList<MonthlyPricePoint> monthlyPoints,
        IReadOnlyList<CorporateAction> actions,
        IReadOnlyList<StockPriceHistory> dailyPrices)
    {
        // Kesirli lot: 2005 öncesi 0.72 lot ≈ 720 adet (eski sistem)
        var lots = buyPrice > 0m ? wage / buyPrice : 0m;
        if (lots < MinLots)
        {
            return new TimeMachineResultDto(
                symbol, mode, 0, 0, 0, 0, 0, buyPrice, monthlyPoints[^1].Price,
                [], [], [], [], dateLabel,
                Error: $"{dateLabel} günü {FormatMoney(wage)} ₺ ile {symbol} alınamıyor (fiyat ~{FormatMoney(buyPrice)} ₺). Tutarı artır.");
        }

        var invested = wage;
        var initialLots = RoundLots(lots);
        lots = initialLots;
        var cash = 0m;
        var dividendsReceived = 0m;
        var dividendsReinvested = 0m;
        var lotsFromReinvest = 0m;
        var actionIdx = 0;

        var series = new List<SimulationPointDto>();
        var valueSeries = new List<decimal>();
        var lotSeries = new List<decimal>();
        var lotEvents = new List<LotEventMarkerDto>();

        foreach (var point in monthlyPoints)
        {
            while (actionIdx < actions.Count &&
                   actions[actionIdx].ActionDate.Date <= point.MonthEnd.Date)
            {
                ApplyAndRecord(
                    actions[actionIdx], point, dailyPrices,
                    ref lots, ref cash,
                    ref dividendsReceived, ref dividendsReinvested, ref lotsFromReinvest,
                    lotEvents);
                actionIdx++;
            }

            series.Add(new SimulationPointDto(point.Year, point.Month, point.Price));
            valueSeries.Add(lots * point.Price + cash);
            lotSeries.Add(RoundLots(lots));
        }

        lots = RoundLots(lots);
        var currentValue = valueSeries[^1];
        var gainPct = invested > 0 ? (currentValue - invested) / invested * 100m : 0m;
        var story = BuildStoryLines(
            symbol, dateLabel, mode, invested, initialLots, lots, buyDate,
            dividendsReceived, dividendsReinvested, lotsFromReinvest, cash, currentValue, lotEvents);

        return new TimeMachineResultDto(
            symbol, mode, invested, currentValue, gainPct, initialLots, lots,
            buyPrice, monthlyPoints[^1].Price, series, valueSeries, lotSeries, lotEvents, dateLabel,
            dividendsReceived, dividendsReinvested, lotsFromReinvest, cash, story);
    }

    private static TimeMachineResultDto CalculateDca(
        string symbol,
        string mode,
        string dateLabel,
        DateTime buyDate,
        decimal buyPrice,
        decimal wagePercentage,
        decimal? amount,
        IReadOnlyList<MonthlyPricePoint> monthlyPoints,
        IReadOnlyList<CorporateAction> actions,
        IReadOnlyList<StockPriceHistory> dailyPrices)
    {
        decimal lots = 0;
        decimal initialLots = 0;
        var cash = 0m;
        var invested = 0m;
        var dividendsReceived = 0m;
        var dividendsReinvested = 0m;
        var lotsFromReinvest = 0m;
        var actionIdx = 0;

        var series = new List<SimulationPointDto>();
        var valueSeries = new List<decimal>();
        var lotSeries = new List<decimal>();
        var lotEvents = new List<LotEventMarkerDto>();

        for (var i = 0; i < monthlyPoints.Count; i++)
        {
            var point = monthlyPoints[i];

            if (i < monthlyPoints.Count - 1)
            {
                var monthlyWage = amount.HasValue && amount.Value > 0
                    ? amount.Value
                    : MinimumWageByYear.Get(point.MonthEnd) * wagePercentage / 100m;
                cash += monthlyWage;
                invested += monthlyWage;

                if (point.Price > 0m)
                {
                    var bought = cash / point.Price;
                    lots += bought;
                    cash -= bought * point.Price;
                    if (initialLots == 0 && lots >= MinLots)
                        initialLots = RoundLots(lots);
                }
            }

            while (actionIdx < actions.Count &&
                   actions[actionIdx].ActionDate.Date <= point.MonthEnd.Date)
            {
                ApplyAndRecord(
                    actions[actionIdx], point, dailyPrices,
                    ref lots, ref cash,
                    ref dividendsReceived, ref dividendsReinvested, ref lotsFromReinvest,
                    lotEvents);
                actionIdx++;
            }

            series.Add(new SimulationPointDto(point.Year, point.Month, point.Price));
            valueSeries.Add(lots * point.Price + cash);
            lotSeries.Add(RoundLots(lots));
        }

        lots = RoundLots(lots);
        if (lots < MinLots)
        {
            return new TimeMachineResultDto(
                symbol, mode, 0, 0, 0, 0, 0, buyPrice, monthlyPoints[^1].Price,
                [], [], [], [], dateLabel,
                Error: "Bu oranla birikim hisse almaya yetmemiş. Oranı artırmayı dene.");
        }

        if (initialLots == 0)
            initialLots = lots;

        var currentValue = valueSeries[^1];
        var gainPct = invested > 0 ? (currentValue - invested) / invested * 100m : 0m;
        var story = BuildStoryLines(
            symbol, dateLabel, mode, invested, initialLots, lots, buyDate,
            dividendsReceived, dividendsReinvested, lotsFromReinvest, cash, currentValue, lotEvents);

        return new TimeMachineResultDto(
            symbol, mode, invested, currentValue, gainPct, initialLots, lots,
            buyPrice, monthlyPoints[^1].Price, series, valueSeries, lotSeries, lotEvents, dateLabel,
            dividendsReceived, dividendsReinvested, lotsFromReinvest, cash, story);
    }

    private static void ApplyAndRecord(
        CorporateAction action,
        MonthlyPricePoint point,
        IReadOnlyList<StockPriceHistory> dailyPrices,
        ref decimal lots,
        ref decimal cash,
        ref decimal dividendsReceived,
        ref decimal dividendsReinvested,
        ref decimal lotsFromReinvest,
        List<LotEventMarkerDto> lotEvents)
    {
        var lotsBefore = RoundLots(lots);
        decimal? cashReceived = null;
        decimal? lotsBought = null;
        string? story = null;

        switch (action.ActionType)
        {
            case CorporateActionType.Dividend:
            {
                var received = lots * action.Value;
                cash += received;
                dividendsReceived += received;
                cashReceived = received;

                var px = FindOnOrAfter(dailyPrices, action.ActionDate)?.Close ?? point.Price;
                if (px > 0m && cash >= px * MinLots)
                {
                    var bought = RoundLots(cash / px);
                    if (bought >= MinLots)
                    {
                        var cost = bought * px;
                        lots += bought;
                        cash -= cost;
                        dividendsReinvested += cost;
                        lotsFromReinvest += bought;
                        lotsBought = bought;
                    }
                }

                story = lotsBought is > 0
                    ? $"{action.ActionDate:d MMMM yyyy}: {received:N2} ₺ temettü → aynı gün +{FormatLots(lotsBought.Value)} lot ({FormatLots(lotsBefore)} → {FormatLots(lots)})"
                    : $"{action.ActionDate:d MMMM yyyy}: {received:N2} ₺ temettü nakit kaldı (fiyat {px:N2} ₺)";
                break;
            }

            case CorporateActionType.BonusIssue:
            {
                if (action.Value <= 0m)
                    return;
                lots = RoundLots(lots * action.Value);
                var pct = (action.Value - 1m) * 100m;
                story =
                    $"{action.ActionDate:d MMMM yyyy}: Bedelsiz %{pct:0.#} (×{action.Value:0.####}) → lot {FormatLots(lotsBefore)} → {FormatLots(lots)}";
                break;
            }

            case CorporateActionType.RightsIssue:
            {
                if (action.Value <= 0m)
                    return;

                var newLots = RoundLots(lotsBefore * action.Value);
                if (newLots < MinLots)
                    return;

                var unitPrice = NormalizeSubscriptionPrice(action.SubscriptionPrice, action.ActionDate);
                var pct = action.Value * 100m;

                if (unitPrice is null or <= 0m)
                {
                    story =
                        $"{action.ActionDate:d MMMM yyyy}: Bedelli %{pct:0.#} hakkı ({FormatLots(newLots)} lot) — rüçhan fiyatı yok, kullanılmadı";
                    lotEvents.Add(new LotEventMarkerDto(
                        point.Year, point.Month,
                        action.ActionDate.ToString("d MMMM yyyy", TrCulture),
                        action.ActionType.ToString(),
                        BuildLotEventLabel(action),
                        lotsBefore, RoundLots(lots), action.Description,
                        Story: story));
                    return;
                }

                var affordable = unitPrice.Value > 0m ? RoundLots(cash / unitPrice.Value) : 0m;
                var bought = Math.Min(newLots, affordable);
                if (bought < MinLots)
                {
                    story =
                        $"{action.ActionDate:d MMMM yyyy}: Bedelli %{pct:0.#} · rüçhan {unitPrice:N4} ₺ — nakit yetersiz, kullanılmadı";
                    lotEvents.Add(new LotEventMarkerDto(
                        point.Year, point.Month,
                        action.ActionDate.ToString("d MMMM yyyy", TrCulture),
                        action.ActionType.ToString(),
                        BuildLotEventLabel(action),
                        lotsBefore, RoundLots(lots), action.Description,
                        Story: story));
                    return;
                }

                var cost = bought * unitPrice.Value;
                cash -= cost;
                lots += bought;
                lotsBought = bought;
                story = bought + MinLots < newLots
                    ? $"{action.ActionDate:d MMMM yyyy}: Bedelli %{pct:0.#} · rüçhan {unitPrice:N4} ₺ → kısmi {FormatLots(bought)}/{FormatLots(newLots)} ({FormatLots(lotsBefore)} → {FormatLots(lots)})"
                    : $"{action.ActionDate:d MMMM yyyy}: Bedelli %{pct:0.#} · rüçhan {unitPrice:N4} ₺ → +{FormatLots(bought)} lot ({FormatLots(lotsBefore)} → {FormatLots(lots)})";
                break;
            }

            default:
                return;
        }

        lots = RoundLots(lots);
        if (lots != lotsBefore || cashReceived is > 0m)
        {
            lotEvents.Add(new LotEventMarkerDto(
                point.Year,
                point.Month,
                action.ActionDate.ToString("d MMMM yyyy", TrCulture),
                action.ActionType.ToString(),
                BuildLotEventLabel(action),
                lotsBefore,
                lots,
                action.Description,
                cashReceived,
                lotsBought,
                story));
        }
    }

    private static List<string> BuildStoryLines(
        string symbol,
        string dateLabel,
        string mode,
        decimal invested,
        decimal initialLots,
        decimal finalLots,
        DateTime buyDate,
        decimal dividendsReceived,
        decimal dividendsReinvested,
        decimal lotsFromReinvest,
        decimal cashRemaining,
        decimal currentValue,
        IReadOnlyList<LotEventMarkerDto> events)
    {
        var lines = new List<string>();

        if (mode == "dca")
        {
            lines.Add(
                $"{dateLabel}'den bugüne düzenli alımla toplam {FormatMoney(invested)} ₺ yatırdın; ilk birikimin ~{FormatLots(initialLots)} lot {symbol}.");
        }
        else if (buyDate.Year < 2005)
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

        var bonus = events.Count(e =>
            e.ActionType == nameof(CorporateActionType.BonusIssue) && e.LotsAfter > e.LotsBefore);
        var rights = events.Count(e =>
            e.ActionType == nameof(CorporateActionType.RightsIssue) && e.LotsAfter > e.LotsBefore);
        var lotsAfterCorp = Math.Max(initialLots, finalLots - lotsFromReinvest);
        if (bonus + rights > 0 && lotsAfterCorp > initialLots + MinLots)
        {
            var parts = new List<string>();
            if (bonus > 0) parts.Add($"{bonus} bedelsiz");
            if (rights > 0) parts.Add($"{rights} bedelli (rüçhan kullanıldı)");
            lines.Add(
                $"{string.Join(" ve ", parts)} ile lotların {FormatLots(initialLots)} → {FormatLots(lotsAfterCorp)} oldu.");
        }

        if (dividendsReceived > 0m)
        {
            lines.Add(
                lotsFromReinvest > 0
                    ? $"Temettü geliri {FormatMoney(dividendsReceived)} ₺; bunun {FormatMoney(dividendsReinvested)} ₺'sini aynı gün hisseye yatırdın (+{FormatLots(lotsFromReinvest)} lot → {FormatLots(finalLots)} lot)."
                    : $"Temettü geliri {FormatMoney(dividendsReceived)} ₺ olarak nakitte kaldı.");
        }
        else if (finalLots > initialLots + MinLots && bonus + rights == 0)
        {
            lines.Add($"Lot sayın {FormatLots(initialLots)} → {FormatLots(finalLots)} oldu.");
        }

        lines.Add(
            cashRemaining > 0.5m
                ? $"Sonuç: elinde {FormatLots(finalLots)} lot · portföy {FormatMoney(currentValue)} ₺ · nakit kalan {FormatMoney(cashRemaining)} ₺."
                : $"Sonuç: elinde {FormatLots(finalLots)} lot · portföy değeri {FormatMoney(currentValue)} ₺.");

        return lines;
    }

    private static string BuildLotEventLabel(CorporateAction action)
        => action.ActionType switch
        {
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

    private static decimal? NormalizeSubscriptionPrice(decimal? raw, DateTime actionDate)
    {
        if (raw is null or <= 0m) return null;
        var price = raw.Value;
        if (actionDate.Date < new DateTime(2005, 1, 1) && price >= 1m)
            price /= 1_000_000m;
        return price;
    }

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
                points.Add(new MonthlyPricePoint(cursor.Year, cursor.Month, monthEnd, monthPrice.Close));

            cursor = cursor.AddMonths(1);
        }

        return points;
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

    private sealed record MonthlyPricePoint(int Year, int Month, DateTime MonthEnd, decimal Price);
}
