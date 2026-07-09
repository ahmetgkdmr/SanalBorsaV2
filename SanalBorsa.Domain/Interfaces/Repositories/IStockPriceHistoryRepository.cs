using SanalBorsa.Domain.Entities;

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

    Task BulkInsertAsync(IEnumerable<StockPriceHistory> records, CancellationToken ct = default);

    Task<bool> AnyAsync(CancellationToken ct = default);
}
