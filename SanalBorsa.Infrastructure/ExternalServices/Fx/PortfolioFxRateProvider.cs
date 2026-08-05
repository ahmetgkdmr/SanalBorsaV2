using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Infrastructure.ExternalServices.Fx;

/// <summary>
/// Anlık USD/TRY kuru — önce Binance'in canlı USDTTRY WS akışından (stablecoin ≈ USD),
/// yoksa DB'deki en son USDTRY (TCMB/TradingView/Yahoo senkronlu) kapanışına düşer.
/// </summary>
public sealed class PortfolioFxRateProvider : IPortfolioFxRateProvider
{
    private readonly ICryptoLiveTickerStore _tickerStore;
    private readonly IUnitOfWork _uow;

    public PortfolioFxRateProvider(ICryptoLiveTickerStore tickerStore, IUnitOfWork uow)
    {
        _tickerStore = tickerStore;
        _uow = uow;
    }

    public async Task<decimal> GetUsdTryRateAsync(CancellationToken ct = default)
    {
        var live = _tickerStore.Get("USDTTRY");
        if (live is not null && live.Price > 0)
            return live.Price;

        var stock = await _uow.Stocks.GetBySymbolAsync("USDTRY", ct);
        if (stock is not null)
        {
            var latest = await _uow.PriceHistories.GetLatestByStockIdAsync(stock.Id, ct);
            if (latest is not null && latest.Close > 0)
                return latest.Close;
        }

        throw new InvalidOperationException(
            "[FX_UNAVAILABLE] Anlık USD/TRY kuru şu an alınamıyor. Lütfen birazdan tekrar deneyin.");
    }
}
