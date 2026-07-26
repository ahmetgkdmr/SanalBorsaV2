using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Infrastructure.ExternalServices.TradingView;

namespace SanalBorsa.Infrastructure.ExternalServices.Bist;

/// <summary>
/// BIST ham günlük fiyat — yalnızca TradingView WebSocket (<c>adjustment=none</c>).
/// </summary>
public class BistRawPriceService : IBistRawPriceService
{
    private readonly TradingViewHistoryClient _tradingView;
    private readonly ILogger<BistRawPriceService> _logger;

    public BistRawPriceService(
        TradingViewHistoryClient tradingView,
        ILogger<BistRawPriceService> logger)
    {
        _tradingView = tradingView;
        _logger = logger;
    }

    public async Task<IReadOnlyList<StockPriceHistory>> GetDailyBarsAsync(
        string bistSymbol,
        DateTime from,
        DateTime to,
        CancellationToken ct = default)
    {
        var symbol = bistSymbol.Trim().ToUpperInvariant();
        var bars = await _tradingView.GetDailyBarsAsync(symbol, from, to, ct);
        if (bars.Count == 0)
        {
            _logger.LogDebug("BIST ham {Symbol}: TradingView boş", symbol);
        }

        return bars;
    }
}
