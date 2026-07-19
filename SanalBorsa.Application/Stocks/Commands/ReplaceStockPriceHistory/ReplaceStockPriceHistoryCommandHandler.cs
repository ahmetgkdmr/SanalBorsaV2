using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Stocks.Commands.ReplaceStockPriceHistory;

public class ReplaceStockPriceHistoryCommandHandler
    : IRequestHandler<ReplaceStockPriceHistoryCommand, ReplaceStockPriceHistoryResult>
{
    private readonly IUnitOfWork _uow;
    private readonly ILogger<ReplaceStockPriceHistoryCommandHandler> _logger;

    public ReplaceStockPriceHistoryCommandHandler(
        IUnitOfWork uow,
        ILogger<ReplaceStockPriceHistoryCommandHandler> logger)
    {
        _uow = uow;
        _logger = logger;
    }

    public async Task<ReplaceStockPriceHistoryResult> Handle(
        ReplaceStockPriceHistoryCommand request,
        CancellationToken cancellationToken)
    {
        var stock = await _uow.Stocks.GetBySymbolAsync(request.Symbol, cancellationToken);
        if (stock is null)
        {
            return new ReplaceStockPriceHistoryResult(
                request.Symbol, 0, null, null, $"Symbol not found: {request.Symbol}");
        }

        if (request.Bars is null || request.Bars.Count == 0)
        {
            return new ReplaceStockPriceHistoryResult(
                request.Symbol, 0, null, null, "No price bars provided");
        }

        await _uow.PriceHistories.DeleteAllByStockIdAsync(stock.Id, cancellationToken);

        var now = DateTime.UtcNow;
        var records = request.Bars
            .Where(b => b.Close > 0)
            .GroupBy(b => b.Date.Date)
            .Select(g =>
            {
                var b = g.Last();
                var close = Math.Round(b.Close, 4);
                return new StockPriceHistory
                {
                    StockId = stock.Id,
                    Date = g.Key,
                    Open = Math.Round(b.Open > 0 ? b.Open : close, 4),
                    High = Math.Round(b.High > 0 ? b.High : close, 4),
                    Low = Math.Round(b.Low > 0 ? b.Low : close, 4),
                    Close = close,
                    AdjustedClose = Math.Round(b.AdjustedClose ?? close, 4),
                    Volume = b.Volume < 0 ? 0 : b.Volume,
                    CreatedAt = now,
                };
            })
            .OrderBy(r => r.Date)
            .ToList();

        if (records.Count == 0)
        {
            return new ReplaceStockPriceHistoryResult(
                request.Symbol, 0, null, null, "All bars filtered out (invalid close)");
        }

        await _uow.PriceHistories.BulkInsertAsync(records, cancellationToken);

        stock.EarliestDataDate = records[0].Date;
        stock.LatestDataDate = records[^1].Date;
        stock.NeedsHistoryRefresh = false;
        stock.UpdatedAt = now;
        _uow.Stocks.Update(stock);
        await _uow.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Replaced price history for {Symbol} from {Source}: {Count} bars ({From:yyyy-MM-dd} → {To:yyyy-MM-dd})",
            stock.Symbol,
            request.Source ?? "import",
            records.Count,
            stock.EarliestDataDate,
            stock.LatestDataDate);

        return new ReplaceStockPriceHistoryResult(
            stock.Symbol,
            records.Count,
            stock.EarliestDataDate,
            stock.LatestDataDate,
            null);
    }
}
