using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Application.Common.Interfaces;

public sealed record TcmbFxBundle(
    IReadOnlyList<StockPriceHistory> UsdTry,
    IReadOnlyList<StockPriceHistory> EurTry,
    IReadOnlyList<StockPriceHistory> GramGold);

/// <summary>TCMB günlük döviz / gram altın (kurlar XML arşivi, API key yok).</summary>
public interface ITcmbFxHistoryService
{
    /// <summary>
    /// Tek geçişte USD, EUR (yoksa DEM→EUR) ve XAU→gram TL.
    /// 2005 öncesi eski TL → Yeni TL (/1e6). Arşiv fiilen ~1999+.
    /// </summary>
    Task<TcmbFxBundle> GetParityBundleAsync(
        DateTime from,
        DateTime to,
        CancellationToken ct = default);
}
