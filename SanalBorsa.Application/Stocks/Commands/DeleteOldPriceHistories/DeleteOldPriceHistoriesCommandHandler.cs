using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Stocks.Commands.DeleteOldPriceHistories;

public class DeleteOldPriceHistoriesCommandHandler
    : IRequestHandler<DeleteOldPriceHistoriesCommand, DeleteOldPriceHistoriesResult>
{
    private readonly IUnitOfWork _uow;
    private readonly ILogger<DeleteOldPriceHistoriesCommandHandler> _logger;

    public DeleteOldPriceHistoriesCommandHandler(
        IUnitOfWork uow,
        ILogger<DeleteOldPriceHistoriesCommandHandler> logger)
    {
        _uow = uow;
        _logger = logger;
    }

    public async Task<DeleteOldPriceHistoriesResult> Handle(
        DeleteOldPriceHistoriesCommand request,
        CancellationToken cancellationToken)
    {
        var cutoff = request.CreatedBeforeUtc;
        var deleted = await _uow.PriceHistories.DeleteCreatedBeforeAsync(cutoff, cancellationToken);

        // Only adjust stocks that still have date metadata set
        var stocks = await _uow.Stocks.GetAllAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var reset = 0;

        foreach (var stock in stocks)
        {
            if (stock.EarliestDataDate is null && stock.LatestDataDate is null)
                continue;

            var earliest = await _uow.PriceHistories.GetEarliestByStockIdAsync(stock.Id, cancellationToken);
            if (earliest is not null)
            {
                var latest = await _uow.PriceHistories.GetLatestByStockIdAsync(stock.Id, cancellationToken);
                var newEarliest = earliest.Date;
                var newLatest = latest?.Date ?? earliest.Date;
                if (stock.EarliestDataDate != newEarliest || stock.LatestDataDate != newLatest)
                {
                    stock.EarliestDataDate = newEarliest;
                    stock.LatestDataDate = newLatest;
                    stock.UpdatedAt = now;
                    _uow.Stocks.Update(stock);
                    reset++;
                }
                continue;
            }

            stock.EarliestDataDate = null;
            stock.LatestDataDate = null;
            stock.NeedsHistoryRefresh = true;
            stock.UpdatedAt = now;
            _uow.Stocks.Update(stock);
            reset++;
        }

        if (reset > 0)
            await _uow.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Deleted price histories CreatedAt < {Cutoff:o}: deleted={Deleted}, stocksAdjusted={Reset}",
            cutoff,
            deleted,
            reset);

        return new DeleteOldPriceHistoriesResult(deleted, cutoff, reset);
    }
}
