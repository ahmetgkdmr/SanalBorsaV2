namespace SanalBorsa.Application.DTOs;

public record TopGainerDto(
    string Period,
    string PeriodLabel,
    string PeriodShortLabel,
    int Rank,
    string Symbol,
    string Name,
    decimal ReturnPct,
    decimal StartPrice,
    decimal EndPrice,
    DateTime StartDate,
    DateTime EndDate,
    decimal? LastClose = null,
    decimal? PreviousClose = null,
    IReadOnlyList<decimal>? Sparkline = null,
    IReadOnlyList<string>? BistIndices = null);

public record TopGainersResponseDto(
    DateTime? AsOfDate,
    DateTime? ComputedAt,
    IReadOnlyList<TopGainerDto> Items);
