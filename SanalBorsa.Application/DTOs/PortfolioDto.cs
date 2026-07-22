using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Application.DTOs;

public record PortfolioDto(
    Guid   Id,
    Guid   UserId,
    decimal CashTry,
    decimal CashUsd,
    IReadOnlyList<HoldingDto> Holdings,
    IReadOnlyList<TransactionDto> Transactions)
{
    public static PortfolioDto FromEntity(UserPortfolio p) => new(
        p.Id,
        p.UserId,
        p.Cash,
        p.CashUsd,
        p.Holdings.Select(h => new HoldingDto(
            h.Symbol,
            h.MarketType == MarketType.Crypto ? "crypto" : "bist",
            h.Quantity,
            h.AvgCost)).ToList(),
        p.Transactions
            .OrderByDescending(t => t.ExecutedAt)
            .Select(t => new TransactionDto(
                t.Id,
                t.Symbol,
                t.MarketType == MarketType.Crypto ? "crypto" : "bist",
                t.Side == TxSide.Buy ? "buy" : "sell",
                t.Quantity,
                t.Price,
                t.Total,
                t.FillBreakdownJson,
                t.ExecutedAt))
            .ToList());
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
    DateTime ExecutedAt);
