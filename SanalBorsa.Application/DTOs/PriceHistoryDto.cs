namespace SanalBorsa.Application.DTOs;

public record PriceHistoryDto(
    DateTime Date,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal AdjustedClose,
    long Volume
);
