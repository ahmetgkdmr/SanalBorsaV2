namespace SanalBorsa.Application.DTOs;

public record SimulationPointDto(int Year, int Month, decimal Price);

/// <summary>
/// Grafiğin gün gün ilerlemesi için sıkıştırılmış günlük seri.
/// Tarih = StartDate + Days[i]; böylece 3-4 bin işlem günü makul boyutta gider.
/// </summary>
public record DailySeriesDto(
    string StartDate,
    IReadOnlyList<int> Days,
    IReadOnlyList<decimal> Prices,
    IReadOnlyList<decimal> Values);

public record TimeMachineResultDto(
    string Symbol,
    string Mode,
    decimal Invested,
    decimal CurrentValue,
    decimal GainPct,
    decimal InitialLots,
    decimal Lots,
    decimal BuyPrice,
    decimal CurrentPrice,
    IReadOnlyList<SimulationPointDto> Series,
    IReadOnlyList<decimal> ValueSeries,
    IReadOnlyList<decimal> LotSeries,
    IReadOnlyList<LotEventMarkerDto> LotEvents,
    string DateLabel,
    decimal DividendsReceived = 0,
    decimal DividendsReinvested = 0,
    decimal LotsFromReinvestment = 0,
    decimal CashRemaining = 0,
    IReadOnlyList<string>? StoryLines = null,
    string? Error = null,
    DailySeriesDto? DailySeries = null);
