using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Stocks.Commands.UpsertStockPriceHistory;

public class UpsertStockPriceHistoryCommandHandler
    : IRequestHandler<UpsertStockPriceHistoryCommand, UpsertStockPriceHistoryResult>
{
    private readonly IUnitOfWork _uow;
    private readonly ILogger<UpsertStockPriceHistoryCommandHandler> _logger;

    public UpsertStockPriceHistoryCommandHandler(
        IUnitOfWork uow,
        ILogger<UpsertStockPriceHistoryCommandHandler> logger)
    {
        _uow = uow;
        _logger = logger;
    }

    public async Task<UpsertStockPriceHistoryResult> Handle(
        UpsertStockPriceHistoryCommand request,
        CancellationToken cancellationToken)
    {
        var stock = await _uow.Stocks.GetBySymbolAsync(request.Symbol, cancellationToken);
        if (stock is null)
        {
            return new UpsertStockPriceHistoryResult(
                request.Symbol, 0, null, null, $"Symbol not found: {request.Symbol}");
        }

        if (request.Bars is null || request.Bars.Count == 0)
        {
            return new UpsertStockPriceHistoryResult(
                request.Symbol, 0, stock.EarliestDataDate, stock.LatestDataDate, "No price bars provided");
        }

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
            return new UpsertStockPriceHistoryResult(
                request.Symbol, 0, stock.EarliestDataDate, stock.LatestDataDate,
                "All bars filtered out (invalid close)");
        }

        var from = records[0].Date;
        var to = records[^1].Date;
        await _uow.PriceHistories.DeleteByStockIdAndDateRangeAsync(stock.Id, from, to, cancellationToken);
        await _uow.PriceHistories.BulkInsertAsync(records, cancellationToken);

        var earliest = await _uow.PriceHistories.GetEarliestByStockIdAsync(stock.Id, cancellationToken);
        var latest = await _uow.PriceHistories.GetLatestByStockIdAsync(stock.Id, cancellationToken);

        stock.EarliestDataDate = earliest?.Date;
        stock.LatestDataDate = latest?.Date;
        stock.NeedsHistoryRefresh = false;
        stock.UpdatedAt = now;
        _uow.Stocks.Update(stock);
        await _uow.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Upserted price history for {Symbol} from {Source}: {Count} bars ({From:yyyy-MM-dd} → {To:yyyy-MM-dd})",
            stock.Symbol,
            request.Source ?? "import",
            records.Count,
            from,
            to);

        return new UpsertStockPriceHistoryResult(
            stock.Symbol,
            records.Count,
            stock.EarliestDataDate,
            stock.LatestDataDate,
            null);
    }
}
