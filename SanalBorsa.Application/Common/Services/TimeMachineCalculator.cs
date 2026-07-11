using System.Globalization;
using SanalBorsa.Application.Common.Constants;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Enums;

namespace SanalBorsa.Application.Common.Services;

public static class TimeMachineCalculator
{
    private static readonly CultureInfo TrCulture = new("tr-TR");

    public static TimeMachineResultDto Calculate(
        string symbol,
        IReadOnlyList<StockPriceHistory> prices,
        IReadOnlyList<CorporateAction> actions,
        DateTime buyDate,
        decimal wagePercentage,
        string mode)
    {
        if (prices.Count == 0)
        {
            return Error(symbol, mode, buyDate, "Bu hisse için fiyat geçmişi bulunamadı.");
        }

        var orderedPrices = prices.OrderBy(p => p.Date).ToList();
        var orderedActions = actions.OrderBy(a => a.ActionDate).ToList();
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
        {
            return Error(symbol, mode, buyDate, "Seçilen tarihte işlem günü bulunamadı.");
        }

        var buyPrice = buyEntry.Close;
        var wage = MinimumWageByYear.Get(buyDate.Year) * wagePercentage / 100m;
        var dateLabel = buyDate.ToString("d MMMM yyyy", TrCulture);
        var normalizedMode = mode.Equals("dca", StringComparison.OrdinalIgnoreCase) ? "dca" : "lump";

        var monthlyPoints = BuildMonthlyPricePoints(orderedPrices, buyDate.Date);
        if (monthlyPoints.Count == 0)
        {
            return Error(symbol, normalizedMode, buyDate, "Simülasyon için yeterli fiyat verisi yok.");
        }

        if (normalizedMode == "lump")
        {
            return CalculateLump(
                symbol,
                normalizedMode,
                dateLabel,
                buyDate,
                buyPrice,
                wage,
                monthlyPoints,
                orderedActions);
        }

        return CalculateDca(
            symbol,
            normalizedMode,
            dateLabel,
            buyDate,
            buyPrice,
            wagePercentage,
            monthlyPoints,
            orderedActions);
    }

    private static TimeMachineResultDto CalculateLump(
        string symbol,
        string mode,
        string dateLabel,
        DateTime buyDate,
        decimal buyPrice,
        decimal wage,
        IReadOnlyList<MonthlyPricePoint> monthlyPoints,
        IReadOnlyList<CorporateAction> actions)
    {
        var lots = (long)Math.Floor(wage / buyPrice);
        if (lots < 1)
        {
            return new TimeMachineResultDto(
                symbol,
                mode,
                0,
                0,
                0,
                0,
                0,
                buyPrice,
                monthlyPoints[^1].Price,
                [],
                [],
                [],
                dateLabel,
                $"Maalesef {dateLabel} günü {Math.Round(wage):N0} ₺ ile {symbol} hissesinden 1 lot bile alamazdın (fiyat ~{buyPrice:F2} ₺). Oranı artır ya da \"Her Ay Düzenli\" modunu dene.");
        }

        var invested = lots * buyPrice;
        var initialLots = lots;
        var cash = 0m;
        var actionIdx = 0;

        var series = new List<SimulationPointDto>();
        var valueSeries = new List<decimal>();
        var lotSeries = new List<long>();

        foreach (var point in monthlyPoints)
        {
            while (actionIdx < actions.Count &&
                   actions[actionIdx].ActionDate.Date <= point.MonthEnd.Date &&
                   actions[actionIdx].ActionDate.Date >= buyDate.Date)
            {
                ApplyCorporateAction(actions[actionIdx], ref lots, ref cash);
                actionIdx++;
            }

            series.Add(new SimulationPointDto(point.Year, point.Month, point.Price));
            valueSeries.Add(lots * point.Price + cash);
            lotSeries.Add(lots);
        }

        var currentValue = valueSeries[^1];
        var gainPct = invested > 0 ? (currentValue - invested) / invested * 100m : 0m;

        return new TimeMachineResultDto(
            symbol,
            mode,
            invested,
            currentValue,
            gainPct,
            initialLots,
            lots,
            buyPrice,
            monthlyPoints[^1].Price,
            series,
            valueSeries,
            lotSeries,
            dateLabel,
            null);
    }

    private static TimeMachineResultDto CalculateDca(
        string symbol,
        string mode,
        string dateLabel,
        DateTime buyDate,
        decimal buyPrice,
        decimal wagePercentage,
        IReadOnlyList<MonthlyPricePoint> monthlyPoints,
        IReadOnlyList<CorporateAction> actions)
    {
        long lots = 0;
        long initialLots = 0;
        var cash = 0m;
        var invested = 0m;
        var actionIdx = 0;

        var series = new List<SimulationPointDto>();
        var valueSeries = new List<decimal>();
        var lotSeries = new List<long>();

        for (var i = 0; i < monthlyPoints.Count; i++)
        {
            var point = monthlyPoints[i];

            if (i < monthlyPoints.Count - 1)
            {
                var monthlyWage = MinimumWageByYear.Get(point.Year) * wagePercentage / 100m;
                cash += monthlyWage;
                invested += monthlyWage;

                var bought = (long)Math.Floor(cash / point.Price);
                lots += bought;
                if (initialLots == 0 && lots > 0)
                {
                    initialLots = lots;
                }
                cash -= bought * point.Price;
            }

            while (actionIdx < actions.Count &&
                   actions[actionIdx].ActionDate.Date <= point.MonthEnd.Date &&
                   actions[actionIdx].ActionDate.Date >= buyDate.Date)
            {
                ApplyCorporateAction(actions[actionIdx], ref lots, ref cash);
                actionIdx++;
            }

            series.Add(new SimulationPointDto(point.Year, point.Month, point.Price));
            valueSeries.Add(lots * point.Price + cash);
            lotSeries.Add(lots);
        }

        if (lots < 1)
        {
            return new TimeMachineResultDto(
                symbol,
                mode,
                0,
                0,
                0,
                0,
                0,
                buyPrice,
                monthlyPoints[^1].Price,
                [],
                [],
                [],
                dateLabel,
                "Bu oranla aylık tutar 1 lota bile yetmemiş. Oranı artırmayı dene.");
        }

        if (initialLots == 0)
        {
            initialLots = lots;
        }

        var currentValue = valueSeries[^1];
        var gainPct = invested > 0 ? (currentValue - invested) / invested * 100m : 0m;

        return new TimeMachineResultDto(
            symbol,
            mode,
            invested,
            currentValue,
            gainPct,
            initialLots,
            lots,
            buyPrice,
            monthlyPoints[^1].Price,
            series,
            valueSeries,
            lotSeries,
            dateLabel,
            null);
    }

    private static void ApplyCorporateAction(CorporateAction action, ref long lots, ref decimal cash)
    {
        switch (action.ActionType)
        {
            case CorporateActionType.Dividend:
                cash += lots * action.Value;
                break;

            case CorporateActionType.BonusIssue:
                lots = action.Value >= 1m
                    ? (long)Math.Floor(lots * action.Value)
                    : (long)Math.Floor(lots * (1 + action.Value));
                break;

            case CorporateActionType.RightsIssue:
                if (action.Value > 0m)
                {
                    lots += (long)Math.Floor(lots * action.Value);
                }
                break;
        }
    }

    private static StockPriceHistory? FindOnOrAfter(
        IReadOnlyList<StockPriceHistory> prices,
        DateTime date)
    {
        return prices.FirstOrDefault(p => p.Date.Date >= date.Date);
    }

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
            {
                monthEnd = latestDate;
            }

            var monthPrice = prices
                .Where(p => p.Date.Year == cursor.Year && p.Date.Month == cursor.Month)
                .OrderByDescending(p => p.Date)
                .FirstOrDefault();

            if (monthPrice is not null)
            {
                points.Add(new MonthlyPricePoint(cursor.Year, cursor.Month, monthEnd, monthPrice.Close));
            }

            cursor = cursor.AddMonths(1);
        }

        return points;
    }

    private static TimeMachineResultDto Error(
        string symbol,
        string mode,
        DateTime buyDate,
        string message)
    {
        return new TimeMachineResultDto(
            symbol,
            mode,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            [],
            [],
            [],
            buyDate.ToString("d MMMM yyyy", TrCulture),
            message);
    }

    private sealed record MonthlyPricePoint(int Year, int Month, DateTime MonthEnd, decimal Price);
}
