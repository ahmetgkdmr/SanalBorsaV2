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
        p.Holdings.Select(h => new HoldingDto(h.Symbol, h.Lots, h.AvgCost)).ToList(),
        p.Transactions
            .OrderByDescending(t => t.ExecutedAt)
            .Select(t => new TransactionDto(
                t.Id, t.Symbol,
                t.Side == TxSide.Buy ? "buy" : "sell",
                t.Lots, t.Price, t.Total, t.ExecutedAt))
            .ToList());
}

public record HoldingDto(string Symbol, long Lots, decimal AvgCost);

public record TransactionDto(
    Guid   Id,
    string Symbol,
    string Side,
    long   Lots,
    decimal Price,
    decimal Total,
    DateTime ExecutedAt);
