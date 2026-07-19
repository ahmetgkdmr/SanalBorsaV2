namespace SanalBorsa.Application.DTOs;

public record SimulationPointDto(int Year, int Month, decimal Price);

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
    string? Error = null);
