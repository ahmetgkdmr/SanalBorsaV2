using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Application.Common.Interfaces;

/// <summary>
/// BIST günlük ham (unadjusted) OHLCV — zaman makinesi corp-action mantığıyla uyumlu.
/// Kaynak: TradingView WebSocket (<c>adjustment=none</c>).
/// </summary>
public interface IBistRawPriceService
{
    /// <summary>
    /// <paramref name="bistSymbol"/> örn. THYAO (BIST: öneki yok).
    /// </summary>
    Task<IReadOnlyList<StockPriceHistory>> GetDailyBarsAsync(
        string bistSymbol,
        DateTime from,
        DateTime to,
        CancellationToken ct = default);
}
