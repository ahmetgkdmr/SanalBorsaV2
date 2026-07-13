namespace SanalBorsa.Application.DTOs;

public record SimulationPointDto(int Year, int Month, decimal Price);

public record TimeMachineResultDto(
    string Symbol,
    string Mode,
    decimal Invested,
    decimal CurrentValue,
    decimal GainPct,
    long InitialLots,
    long Lots,
    decimal BuyPrice,
    decimal CurrentPrice,
    IReadOnlyList<SimulationPointDto> Series,
    IReadOnlyList<decimal> ValueSeries,
    IReadOnlyList<long> LotSeries,
    IReadOnlyList<LotEventMarkerDto> LotEvents,
    string DateLabel,
    string? Error);
