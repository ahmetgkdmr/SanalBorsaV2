namespace SanalBorsa.Application.DTOs;

public record PublicTradeHistoryDto(
    string Username,
    /// <summary>Kullanıcı geçmişini paylaşmıyorsa false ve liste boş döner.</summary>
    bool IsPublic,
    IReadOnlyList<PublicTradeDto> Trades);

public record PublicTradeDto(
    string Symbol,
    string MarketType,
    string Side,
    decimal Quantity,
    decimal Price,
    DateTime ExecutedAt);
