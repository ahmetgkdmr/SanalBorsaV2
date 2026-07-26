using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Application.Common.Interfaces;

/// <summary>
/// BIST günlük ham (unadjusted) OHLCV — TradingView <c>adjustment=none</c>.
/// </summary>
public interface IBistRawPriceService
{
    /// <summary><paramref name="bistSymbol"/> örn. THYAO (BIST: öneki yok).</summary>
    Task<IReadOnlyList<StockPriceHistory>> GetDailyBarsAsync(
        string bistSymbol,
        DateTime from,
        DateTime to,
        CancellationToken ct = default);

    /// <summary>
    /// TradingView düzeltilmiş kapanış (<c>adjustment=dividends</c>) — yalnızca tarih→fiyat.
    /// Mevcut ham satırlara <c>AdjustedClose</c> yazmak için.
    /// </summary>
    Task<IReadOnlyDictionary<DateTime, decimal>> GetAdjustedClosesAsync(
        string bistSymbol,
        DateTime from,
        DateTime to,
        CancellationToken ct = default);
}
