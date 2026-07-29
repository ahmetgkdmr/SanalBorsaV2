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
        var now = DateTime.UtcNow;

        foreach (var entry in request.Add)
        {
            var symbol = entry.Symbol.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(symbol))
                continue;

            var existing = await _uow.Stocks.GetBySymbolAsync(symbol, cancellationToken);
            if (existing is not null)
            {
                if (!existing.IsActive)
                {
                    existing.IsActive = true;
                    existing.UpdatedAt = now;
                    if (!string.IsNullOrWhiteSpace(entry.Name))
                        existing.Name = entry.Name.Trim();
                    _uow.Stocks.Update(existing);
                    added.Add(symbol);
                }
                else
                {
                    skippedExisting++;
                }
                continue;
            }

            var stock = new Stock
            {
                Symbol = symbol,
                YahooSymbol = $"{symbol}.IS",
                Name = string.IsNullOrWhiteSpace(entry.Name) ? symbol : entry.Name.Trim(),
                Currency = "TRY",
                Exchange = "IST",
                MarketType = MarketType.Bist,
                IsActive = true,
                NeedsHistoryRefresh = true,
                CreatedAt = now,
                UpdatedAt = now,
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

            // Never deactivate seeded market instruments via this path
            if (MarketInstrumentSeed.IsMarketInstrument(stock.Exchange))
            {
                _logger.LogWarning("Skip deactivating market instrument {Symbol}", symbol);
                skippedMissing++;
                continue;
            }

            if (!stock.IsActive)
            {
                skippedMissing++;
                continue;
            }

            // Soft-delete: pasife çek, fiyat / corporate action silme
            stock.IsActive = false;
            stock.UpdatedAt = now;
            _uow.Stocks.Update(stock);
            removed.Add(symbol);
            _logger.LogWarning(
                "BIST soft-deactivate (universe remove): {Symbol} — fiyat geçmişi korundu",
                symbol);
        }

        if (removed.Count > 0)
            await _uow.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Universe sync — added/reactivated={Added} softDeactivated={Removed} skipExisting={SkipEx} skipMissing={SkipMis}",
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
