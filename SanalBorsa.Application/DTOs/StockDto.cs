namespace SanalBorsa.Application.DTOs;

public record StockDto(
    int Id,
    string Symbol,
    string Name,
    string? Sector,
    string? Industry,
    string Currency,
    string Exchange,
    bool IsActive,
    DateTime? EarliestDataDate,
    DateTime? LatestDataDate,
    bool NeedsHistoryRefresh
);
