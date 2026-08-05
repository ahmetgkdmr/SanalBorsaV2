using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Application.DTOs;

public record PortfolioDto(
    Guid   Id,
    Guid   UserId,
    decimal Cash,
    IReadOnlyList<HoldingDto> Holdings,
    IReadOnlyList<TransactionDto> Transactions)
{
    public static PortfolioDto FromEntity(UserPortfolio p) => new(
        p.Id,
        p.UserId,
        p.Cash,
        p.Holdings.Select(h => new HoldingDto(
            h.Symbol,
            MarketTypeToString(h.MarketType),
            h.Quantity,
            h.AvgCost)).ToList(),
        // İşlem geçmişi ayrı endpoint'ten sayfalanır; buraya yüklenmez.
        Array.Empty<TransactionDto>());

    public static string MarketTypeToString(MarketType mt) => mt switch
    {
        MarketType.Crypto   => "crypto",
        MarketType.UsStocks => "us",
        _                   => "bist",
    };
}

public record HoldingDto(
    string Symbol,
    string MarketType,
    decimal Quantity,
    decimal AvgCost);

public record TransactionDto(
    Guid   Id,
    string Symbol,
    string MarketType,
    string Side,
    decimal Quantity,
    decimal Price,
    decimal Total,
    string? FillBreakdownJson,
    decimal? ExchangeRateAtTrade,
    DateTime ExecutedAt);
