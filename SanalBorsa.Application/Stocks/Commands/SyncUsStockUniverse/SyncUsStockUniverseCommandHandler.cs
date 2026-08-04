using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Application.Common.Seeds;
using SanalBorsa.Application.Common.Services;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Stocks.Commands.SyncUsStockUniverse;

public class SyncUsStockUniverseCommandHandler
    : IRequestHandler<SyncUsStockUniverseCommand, SyncUsStockUniverseResult>
{
    private readonly IUnitOfWork _uow;
    private readonly IYahooFinanceService _yahoo;
    private readonly ILogger<SyncUsStockUniverseCommandHandler> _logger;

    public SyncUsStockUniverseCommandHandler(
        IUnitOfWork uow,
        IYahooFinanceService yahoo,
        ILogger<SyncUsStockUniverseCommandHandler> logger)
    {
        _uow = uow;
        _yahoo = yahoo;
        _logger = logger;
    }

    public async Task<SyncUsStockUniverseResult> Handle(
        SyncUsStockUniverseCommand request,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var added = 0;
        var existing = 0;

        foreach (var symbol in UsStockSymbolSeed.Symbols)
        {
            var stock = await _uow.Stocks.GetBySymbolAsync(symbol, cancellationToken, MarketType.UsStocks);
            if (stock is not null)
            {
                existing++;
                continue;
            }

            // Fiyat artık TradingView'dan çekiliyor — doğru "NASDAQ:AAPL" gibi sembolü kurmak için
            // gerçek borsayı Yahoo'nun metadata'sından çözüyoruz (bkz. UsExchangeResolver).
            var meta = await _yahoo.GetStockMetadataAsync(symbol, cancellationToken);
            var tvExchange = UsExchangeResolver.ToTvPrefix(meta?.Exchange);

            await _uow.Stocks.AddAsync(new Stock
            {
                Symbol = symbol,
                YahooSymbol = symbol,
                Name = meta?.LongName ?? symbol,
                Currency = "USD",
                Exchange = tvExchange,
                MarketType = MarketType.UsStocks,
                IsActive = true,
                NeedsHistoryRefresh = true,
                CreatedAt = now,
                UpdatedAt = now,
            }, cancellationToken);
            added++;
        }

        if (added > 0)
            await _uow.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "ABD hisse evreni sync — added={Added} alreadyExisting={Existing}", added, existing);

        return new SyncUsStockUniverseResult(added, existing);
    }
}
