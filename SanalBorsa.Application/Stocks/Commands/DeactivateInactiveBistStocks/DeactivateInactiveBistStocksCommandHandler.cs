using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Application.Common.Seeds;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Stocks.Commands.DeactivateInactiveBistStocks;

public class DeactivateInactiveBistStocksCommandHandler
    : IRequestHandler<DeactivateInactiveBistStocksCommand, DeactivateInactiveBistStocksResult>
{
    private const int DelayMs = 40;

    private readonly IUnitOfWork _uow;
    private readonly IBistRawPriceService _prices;
    private readonly ILogger<DeactivateInactiveBistStocksCommandHandler> _logger;

    public DeactivateInactiveBistStocksCommandHandler(
        IUnitOfWork uow,
        IBistRawPriceService prices,
        ILogger<DeactivateInactiveBistStocksCommandHandler> logger)
    {
        _uow = uow;
        _prices = prices;
        _logger = logger;
    }

    public async Task<DeactivateInactiveBistStocksResult> Handle(
        DeactivateInactiveBistStocksCommand request,
        CancellationToken cancellationToken)
    {
        var lookback = request.LookbackDays > 0 ? request.LookbackDays : 60;
        var to = DateTime.UtcNow.Date;
        var from = to.AddDays(-lookback);

        var stocks = (await _uow.Stocks.GetAllActiveAsync(cancellationToken, MarketType.Bist))
            .Where(s => s.MarketType == MarketType.Bist)
            .Where(s => !MarketInstrumentSeed.IsMarketInstrument(s.Exchange))
            .Where(s => s.Exchange is "IST" or "BIST")
            .OrderBy(s => s.Symbol)
            .ToList();

        if (stocks.Count == 0)
            return new DeactivateInactiveBistStocksResult(0, 0, 0, [], "Aktif BIST hissesi yok.");

        var deactivated = new List<string>();
        var failedProbe = 0;
        var done = 0;
        var now = DateTime.UtcNow;

        foreach (var stock in stocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            done++;

            try
            {
                var bars = await _prices.GetDailyBarsAsync(stock.Symbol, from, to, cancellationToken);
                if (bars.Count > 0)
                {
                    if (done % 50 == 0 || done == stocks.Count)
                    {
                        _logger.LogInformation(
                            "BIST inactive probe progress: {Done}/{Total} — {Symbol} OK ({Bars})",
                            done, stocks.Count, stock.Symbol, bars.Count);
                    }
                }
                else
                {
                    // TradingView boş → işlem görmüyor / delist / çözülemiyor → soft pasif
                    stock.IsActive = false;
                    stock.UpdatedAt = now;
                    _uow.Stocks.Update(stock);
                    deactivated.Add(stock.Symbol);
                    _logger.LogWarning(
                        "BIST soft-deactivate (TV boş): {Symbol} — fiyat geçmişi korundu",
                        stock.Symbol);
                }
            }
            catch (Exception ex)
            {
                failedProbe++;
                _logger.LogError(ex, "BIST inactive probe failed for {Symbol}", stock.Symbol);
            }

            if (DelayMs > 0 && done < stocks.Count)
                await Task.Delay(DelayMs, cancellationToken);
        }

        if (deactivated.Count > 0)
            await _uow.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "BIST inactive reconcile done — checked={C} deactivated={D} probeFail={F}: {Symbols}",
            stocks.Count, deactivated.Count, failedProbe, string.Join(',', deactivated));

        return new DeactivateInactiveBistStocksResult(
            stocks.Count, deactivated.Count, failedProbe, deactivated, null);
    }
}
