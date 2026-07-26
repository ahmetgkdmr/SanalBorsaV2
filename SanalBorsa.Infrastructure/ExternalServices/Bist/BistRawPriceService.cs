using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Infrastructure.ExternalServices.TradingView;

namespace SanalBorsa.Infrastructure.ExternalServices.Bist;

/// <summary>
/// BIST fiyat — TradingView WebSocket.
/// Ham: <c>adjustment=none</c>; AdjustedClose: <c>adjustment=dividends</c>.
/// </summary>
public class BistRawPriceService : IBistRawPriceService
{
    private readonly ITradingViewHistoryService _tradingView;
    private readonly ILogger<BistRawPriceService> _logger;

    public BistRawPriceService(
        ITradingViewHistoryService tradingView,
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
        var bars = await _tradingView.GetBistDailyBarsAsync(symbol, from, to, ct);
        if (bars.Count == 0)
            _logger.LogDebug("BIST ham {Symbol}: TradingView boş", symbol);
        return bars;
    }

    public async Task<IReadOnlyDictionary<DateTime, decimal>> GetAdjustedClosesAsync(
        string bistSymbol,
        DateTime from,
        DateTime to,
        CancellationToken ct = default)
    {
        var symbol = bistSymbol.Trim().ToUpperInvariant();
        var map = await _tradingView.GetBistAdjustedClosesAsync(symbol, from, to, ct);
        if (map.Count == 0)
            _logger.LogDebug("BIST AdjustedClose {Symbol}: TradingView boş", symbol);
        return map;
    }
}
