using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Models;

namespace SanalBorsa.Domain.Interfaces.Repositories;

public interface IStockPriceHistoryRepository : IRepository<StockPriceHistory>
{
    Task<IReadOnlyList<StockPriceHistory>> GetByStockIdAsync(
        int stockId,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default);

    Task<StockPriceHistory?> GetLatestByStockIdAsync(int stockId, CancellationToken ct = default);

    Task<StockPriceHistory?> GetEarliestByStockIdAsync(int stockId, CancellationToken ct = default);

    Task DeleteAllByStockIdAsync(int stockId, CancellationToken ct = default);

    Task DeleteByStockIdAndDateRangeAsync(
        int stockId,
        DateTime fromInclusive,
        DateTime toInclusive,
        CancellationToken ct = default);

    Task<int> DeleteAllAsync(CancellationToken ct = default);

    /// <summary>Deletes price rows with CreatedAt strictly before the given UTC timestamp (non-TV leftovers).</summary>
    Task<int> DeleteCreatedBeforeAsync(DateTime createdBeforeUtc, CancellationToken ct = default);

    Task BulkInsertAsync(IEnumerable<StockPriceHistory> records, CancellationToken ct = default);

    Task<bool> AnyAsync(CancellationToken ct = default);

    Task<IReadOnlyDictionary<int, MarketPriceSnapshot>> GetMarketSnapshotsAsync(
        IReadOnlyList<int> stockIds,
        int sparklineDays = 28,
        CancellationToken ct = default,
        int? windowDays = null);

    /// <summary>Global max trading date in price history (last close day).</summary>
    Task<DateTime?> GetLatestTradingDateAsync(CancellationToken ct = default);

    /// <summary>
    /// For each stock: close on the latest session on or before <paramref name="onOrBefore"/>.
    /// </summary>
    Task<IReadOnlyDictionary<int, (DateTime Date, decimal Close)>> GetClosesOnOrBeforeAsync(
        IReadOnlyList<int> stockIds,
        DateTime onOrBefore,
        CancellationToken ct = default);
}
