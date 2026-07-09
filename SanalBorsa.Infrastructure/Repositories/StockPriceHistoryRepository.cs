using Microsoft.EntityFrameworkCore;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces.Repositories;
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
        var list = records.ToList();

        for (int i = 0; i < list.Count; i += chunkSize)
        {
            var chunk = list.Skip(i).Take(chunkSize);
            await DbSet.AddRangeAsync(chunk, ct);
            await Context.SaveChangesAsync(ct);
        }
    }
}
