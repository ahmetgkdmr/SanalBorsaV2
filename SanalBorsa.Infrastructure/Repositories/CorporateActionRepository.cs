using Microsoft.EntityFrameworkCore;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Enums;
using SanalBorsa.Domain.Interfaces.Repositories;
using SanalBorsa.Infrastructure.Data;

namespace SanalBorsa.Infrastructure.Repositories;

public class CorporateActionRepository : BaseRepository<CorporateAction>, ICorporateActionRepository
{
    public CorporateActionRepository(AppDbContext context) : base(context) { }

    public async Task<IReadOnlyList<CorporateAction>> GetByStockIdAsync(int stockId, CancellationToken ct = default)
        => await DbSet
            .Where(a => a.StockId == stockId)
            .OrderByDescending(a => a.ActionDate)
            .ToListAsync(ct);

    public async Task<bool> ExistsAsync(int stockId, DateTime date, CorporateActionType type, CancellationToken ct = default)
        => await DbSet.AnyAsync(
            a => a.StockId == stockId && a.ActionDate == date.Date && a.ActionType == type, ct);

    public async Task<DateTime?> GetLatestActionDateAsync(int stockId, CancellationToken ct = default)
        => await DbSet
            .Where(a => a.StockId == stockId)
            .Select(a => (DateTime?)a.ActionDate)
            .MaxAsync(ct);

    public async Task<int> DeleteAllByStockIdAsync(int stockId, CancellationToken ct = default)
    {
        var existing = await DbSet.Where(a => a.StockId == stockId).ToListAsync(ct);
        if (existing.Count == 0)
            return 0;

        DbSet.RemoveRange(existing);
        return existing.Count;
    }

    public async Task<int> DeleteAllAsync(CancellationToken ct = default)
        => await DbSet.ExecuteDeleteAsync(ct);

    public async Task<IReadOnlyList<CorporateAction>> GetByStockIdAndTypeAsync(
        int stockId,
        CorporateActionType type,
        CancellationToken ct = default)
        => await DbSet
            .Where(a => a.StockId == stockId && a.ActionType == type)
            .OrderByDescending(a => a.ActionDate)
            .ToListAsync(ct);
}
