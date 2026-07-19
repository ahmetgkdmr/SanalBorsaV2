using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Seeds;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Stocks.Commands.SyncStockUniverse;

public class SyncStockUniverseCommandHandler
    : IRequestHandler<SyncStockUniverseCommand, SyncStockUniverseResult>
{
    private readonly IUnitOfWork _uow;
    private readonly ILogger<SyncStockUniverseCommandHandler> _logger;

    public SyncStockUniverseCommandHandler(
        IUnitOfWork uow,
        ILogger<SyncStockUniverseCommandHandler> logger)
    {
        _uow = uow;
        _logger = logger;
    }

    public async Task<SyncStockUniverseResult> Handle(
        SyncStockUniverseCommand request,
        CancellationToken cancellationToken)
    {
        var added = new List<string>();
        var removed = new List<string>();
        var skippedExisting = 0;
        var skippedMissing = 0;

        foreach (var entry in request.Add)
        {
            var symbol = entry.Symbol.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(symbol))
                continue;

            if (await _uow.Stocks.ExistsAsync(symbol, cancellationToken))
            {
                skippedExisting++;
                continue;
            }

            var stock = new Stock
            {
                Symbol = symbol,
                YahooSymbol = $"{symbol}.IS",
                Name = string.IsNullOrWhiteSpace(entry.Name) ? symbol : entry.Name.Trim(),
                Currency = "TRY",
                Exchange = "IST",
                IsActive = true,
                NeedsHistoryRefresh = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };

            await _uow.Stocks.AddAsync(stock, cancellationToken);
            added.Add(symbol);
        }

        if (added.Count > 0)
            await _uow.SaveChangesAsync(cancellationToken);

        foreach (var raw in request.Remove)
        {
            var symbol = raw.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(symbol))
                continue;

            var stock = await _uow.Stocks.GetBySymbolAsync(symbol, cancellationToken);
            if (stock is null)
            {
                skippedMissing++;
                continue;
            }

            // Never delete seeded market instruments via this path
            if (MarketInstrumentSeed.IsMarketInstrument(stock.Exchange))
            {
                _logger.LogWarning("Skip removing market instrument {Symbol}", symbol);
                skippedMissing++;
                continue;
            }

            await _uow.PriceHistories.DeleteAllByStockIdAsync(stock.Id, cancellationToken);
            // Corporate actions cascade with stock delete via EF config
            _uow.Stocks.Remove(stock);
            removed.Add(symbol);
        }

        if (removed.Count > 0)
            await _uow.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Universe sync — added={Added} removed={Removed} skipExisting={SkipEx} skipMissing={SkipMis}",
            added.Count, removed.Count, skippedExisting, skippedMissing);

        return new SyncStockUniverseResult(
            added.Count,
            removed.Count,
            skippedExisting,
            skippedMissing,
            added,
            removed);
    }
}
