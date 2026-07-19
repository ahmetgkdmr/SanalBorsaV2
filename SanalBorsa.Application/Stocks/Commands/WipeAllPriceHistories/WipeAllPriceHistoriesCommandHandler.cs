using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Stocks.Commands.WipeAllPriceHistories;

public class WipeAllPriceHistoriesCommandHandler
    : IRequestHandler<WipeAllPriceHistoriesCommand, WipeAllPriceHistoriesResult>
{
    private readonly IUnitOfWork _uow;
    private readonly ILogger<WipeAllPriceHistoriesCommandHandler> _logger;

    public WipeAllPriceHistoriesCommandHandler(
        IUnitOfWork uow,
        ILogger<WipeAllPriceHistoriesCommandHandler> logger)
    {
        _uow = uow;
        _logger = logger;
    }

    public async Task<WipeAllPriceHistoriesResult> Handle(
        WipeAllPriceHistoriesCommand request,
        CancellationToken cancellationToken)
    {
        var deleted = await _uow.PriceHistories.DeleteAllAsync(cancellationToken);

        // Tarih alanlarını per-entity update ile değil, repository tarafındaki
        // TRUNCATE sonrası stok reset'i için lightweight dolaşım
        var stocks = await _uow.Stocks.GetAllAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var n = 0;
        foreach (var stock in stocks)
        {
            stock.EarliestDataDate = null;
            stock.LatestDataDate = null;
            stock.NeedsHistoryRefresh = true;
            stock.UpdatedAt = now;
            _uow.Stocks.Update(stock);
            n++;
            if (n % 100 == 0)
                await _uow.SaveChangesAsync(cancellationToken);
        }

        await _uow.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "All price histories wiped — deleted={Deleted}, stocksReset={Count}",
            deleted,
            stocks.Count);

        return new WipeAllPriceHistoriesResult(deleted < 0 ? stocks.Count : deleted);
    }
}
