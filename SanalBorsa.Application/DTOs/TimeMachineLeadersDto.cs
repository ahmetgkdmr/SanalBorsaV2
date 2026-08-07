namespace SanalBorsa.Application.DTOs;

/// <summary>
/// Seçilen günden bugüne bir enstrümanın getirdiği sonuç.
/// <c>Multiple</c> paranın kaça katlandığıdır (EndPrice / StartPrice).
/// </summary>
public record TimeMachineLeaderDto(
    int Rank,
    string Symbol,
    string Name,
    decimal StartPrice,
    decimal EndPrice,
    decimal ReturnPct,
    decimal Multiple,
    string StartDate,
    string EndDate);

/// <summary>
/// "O gün başka ne alsaydın?" — seçilen tarih için önceden hesaplanmış
/// BIST top-5, kripto top-5, ABD top-5 ve üç TL paritesi.
/// </summary>
public record TimeMachineLeadersDto(
    string RequestedDate,
    IReadOnlyList<TimeMachineLeaderDto> Bist,
    IReadOnlyList<TimeMachineLeaderDto> Crypto,
    IReadOnlyList<TimeMachineLeaderDto> UsStocks,
    IReadOnlyList<TimeMachineLeaderDto> Parity,
    DateTime? ComputedAt);
