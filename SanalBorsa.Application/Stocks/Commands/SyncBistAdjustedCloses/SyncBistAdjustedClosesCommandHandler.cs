using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Application.Common.Seeds;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Stocks.Commands.SyncBistAdjustedCloses;

public class SyncBistAdjustedClosesCommandHandler
    : IRequestHandler<SyncBistAdjustedClosesCommand, SyncBistAdjustedClosesResult>
{
    private const int DelayMs = 40;

    private readonly IUnitOfWork _uow;
    private readonly IBistRawPriceService _prices;
    private readonly ILogger<SyncBistAdjustedClosesCommandHandler> _logger;

    public SyncBistAdjustedClosesCommandHandler(
        IUnitOfWork uow,
        IBistRawPriceService prices,
        ILogger<SyncBistAdjustedClosesCommandHandler> logger)
    {
        _uow = uow;
        _prices = prices;
        _logger = logger;
    }

    public async Task<SyncBistAdjustedClosesResult> Handle(
        SyncBistAdjustedClosesCommand request,
        CancellationToken cancellationToken)
    {
        List<Stock> stocks;

        if (!string.IsNullOrWhiteSpace(request.Symbol))
        {
            var one = await _uow.Stocks.GetBySymbolAsync(
                request.Symbol.Trim().ToUpperInvariant(), cancellationToken);
            stocks = one is null ? [] : [one];
        }
        else
        {
            stocks = (await _uow.Stocks.GetAllActiveAsync(cancellationToken, MarketType.Bist))
                .Where(s => s.MarketType == MarketType.Bist)
                .Where(s => !MarketInstrumentSeed.IsMarketInstrument(s.Exchange))
                .Where(s => s.Exchange is "IST" or "BIST")
                .Where(s => s.EarliestDataDate is not null)
                .OrderBy(s => s.Symbol)
                .ToList();
        }

        if (stocks.Count == 0)
            return new SyncBistAdjustedClosesResult(0, 0, 0, 0, "Güncellenecek BIST hissesi yok.");

        var to = DateTime.UtcNow.Date;
        var synced = 0;
        var rowsUpdated = 0;
        var failed = 0;
        var done = 0;

        foreach (var stock in stocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            done++;

            try
            {
                var from = stock.EarliestDataDate?.Date
                           ?? stock.LatestDataDate?.Date
                           ?? DateTime.UnixEpoch;

                if (from > to)
                {
                    synced++;
                    continue;
                }

                var adj = await _prices.GetAdjustedClosesAsync(stock.Symbol, from, to, cancellationToken);
                if (adj.Count == 0)
                {
                    failed++;
                    _logger.LogWarning("BIST AdjustedClose sync: {Symbol} için TV boş", stock.Symbol);
                    continue;
                }

                var updated = await _uow.PriceHistories.UpdateAdjustedClosesAsync(
                    stock.Id, adj, cancellationToken);
                _uow.ClearChanges();

                synced++;
                rowsUpdated += updated;

                if (done % 25 == 0 || done == stocks.Count)
                {
                    _logger.LogInformation(
                        "BIST AdjustedClose progress: {Done}/{Total} — {Symbol} (+{Rows})",
                        done, stocks.Count, stock.Symbol, updated);
                }
            }
            catch (Exception ex)
            {
                failed++;
                _uow.ClearChanges();
                _logger.LogError(ex, "BIST AdjustedClose sync failed for {Symbol}", stock.Symbol);
            }

            if (DelayMs > 0 && done < stocks.Count)
                await Task.Delay(DelayMs, cancellationToken);
        }

        _logger.LogInformation(
            "BIST AdjustedClose sync done — attempted={A} synced={S} rows={R} failed={F}",
            stocks.Count, synced, rowsUpdated, failed);

        return new SyncBistAdjustedClosesResult(stocks.Count, synced, rowsUpdated, failed, null);
    }
}
