using Microsoft.EntityFrameworkCore;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces.Repositories;
using SanalBorsa.Domain.Models;
using SanalBorsa.Infrastructure.Data;

namespace SanalBorsa.Infrastructure.Repositories;

public class StockPriceHistoryRepository : BaseRepository<StockPriceHistory>, IStockPriceHistoryRepository
{
    public StockPriceHistoryRepository(AppDbContext context) : base(context) { }

    public async Task<IReadOnlyList<StockPriceHistory>> GetByStockIdAsync(
        int stockId,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default)
    {
        var query = DbSet.Where(p => p.StockId == stockId);

        if (from.HasValue)
            query = query.Where(p => p.Date >= from.Value.Date);

        if (to.HasValue)
            query = query.Where(p => p.Date <= to.Value.Date);

        return await query
            .OrderBy(p => p.Date)
            .ToListAsync(ct);
    }

    public async Task<StockPriceHistory?> GetLatestByStockIdAsync(int stockId, CancellationToken ct = default)
        => await DbSet
            .Where(p => p.StockId == stockId)
            .OrderByDescending(p => p.Date)
            .FirstOrDefaultAsync(ct);

    public async Task<StockPriceHistory?> GetEarliestByStockIdAsync(int stockId, CancellationToken ct = default)
        => await DbSet
            .Where(p => p.StockId == stockId)
            .OrderBy(p => p.Date)
            .FirstOrDefaultAsync(ct);

    public async Task DeleteAllByStockIdAsync(int stockId, CancellationToken ct = default)
        => await DbSet
            .Where(p => p.StockId == stockId)
            .ExecuteDeleteAsync(ct);

    public async Task<bool> AnyAsync(CancellationToken ct = default)
        => await DbSet.AnyAsync(ct);

    public async Task BulkInsertAsync(IEnumerable<StockPriceHistory> records, CancellationToken ct = default)
    {
        // EF Core 8 ExecuteInsert is not available; batch via chunked AddRange + SaveChanges
        const int chunkSize = 2000;
        var list = records
            .GroupBy(r => new { r.StockId, Date = r.Date.Date })
            .Select(g => g.Last())
            .ToList();

        for (int i = 0; i < list.Count; i += chunkSize)
        {
            var chunk = list.Skip(i).Take(chunkSize);
            await DbSet.AddRangeAsync(chunk, ct);
            await Context.SaveChangesAsync(ct);
        }
    }

    public async Task<IReadOnlyDictionary<int, MarketPriceSnapshot>> GetMarketSnapshotsAsync(
        IReadOnlyList<int> stockIds,
        int sparklineDays = 28,
        CancellationToken ct = default,
        int? windowDays = null)
    {
        if (stockIds.Count == 0)
            return new Dictionary<int, MarketPriceSnapshot>();

        // windowDays: kaç gün geriye bakılacak. Belirtilmezse sparklineDays + 5 gün.
        var effectiveWindow = windowDays ?? (sparklineDays + 5);
        var from = DateTime.UtcNow.Date.AddDays(-effectiveWindow);
        var records = await DbSet
            .AsNoTracking()
            .Where(p => stockIds.Contains(p.StockId) && p.Date >= from)
            .OrderBy(p => p.StockId)
            .ThenBy(p => p.Date)
            .ToListAsync(ct);

        var result = records
            .GroupBy(p => p.StockId)
            .ToDictionary(g => g.Key, g => BuildSnapshot(g.ToList(), sparklineDays));

        // Pencere içinde kaydı olmayan stock'lar için en son kaydı fallback olarak al
        var missingIds = stockIds.Where(id => !result.ContainsKey(id)).ToList();
        if (missingIds.Count > 0)
        {
            // Her eksik stock için son 28 kaydı çek; çok veri yüklemeden sparkline + son fiyat oluşturulur
            foreach (var missingId in missingIds)
            {
                var fallback = await DbSet
                    .AsNoTracking()
                    .Where(p => p.StockId == missingId)
                    .OrderByDescending(p => p.Date)
                    .Take(sparklineDays + 2)
                    .OrderBy(p => p.Date)
                    .ToListAsync(ct);

                if (fallback.Count > 0)
                    result[missingId] = BuildSnapshot(fallback, sparklineDays);
            }
        }

        return result;
    }

    private static MarketPriceSnapshot BuildSnapshot(IReadOnlyList<StockPriceHistory> ordered, int sparklineDays)
    {
        if (ordered.Count == 0)
            return new MarketPriceSnapshot(null, null, null, null, []);

        var latest = ordered[^1];
        var previous = ordered.Count > 1 ? ordered[^2] : null;
        var sparkline = ordered
            .TakeLast(sparklineDays)
            .Select(p => p.Close)
            .ToList();

        return new MarketPriceSnapshot(
            latest.Close,
            latest.Open,
            previous?.Close,
            latest.Volume,
            sparkline);
    }
}
