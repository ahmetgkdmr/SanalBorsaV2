using Microsoft.EntityFrameworkCore;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces.Repositories;
using SanalBorsa.Infrastructure.Data;

namespace SanalBorsa.Infrastructure.Repositories;

public class StockRepository : BaseRepository<Stock>, IStockRepository
{
    public StockRepository(AppDbContext context) : base(context) { }

    public async Task<Stock?> GetBySymbolAsync(string symbol, CancellationToken ct = default)
        => await DbSet
            .FirstOrDefaultAsync(s => s.Symbol == symbol, ct);

    public async Task<IReadOnlyList<Stock>> GetAllActiveAsync(CancellationToken ct = default)
        => await DbSet
            .Where(s => s.IsActive)
            .OrderBy(s => s.Symbol)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Stock>> GetStocksNeedingRefreshAsync(CancellationToken ct = default)
        => await DbSet
            .Where(s => s.IsActive && (s.NeedsHistoryRefresh || s.EarliestDataDate == null))
            .OrderBy(s => s.Symbol)
            .ToListAsync(ct);

    public async Task<bool> ExistsAsync(string symbol, CancellationToken ct = default)
        => await DbSet.AnyAsync(s => s.Symbol == symbol, ct);

    public async Task<IReadOnlyList<Stock>> GetBySymbolsAsync(
        IReadOnlyList<string> symbols,
        CancellationToken ct = default)
    {
        if (symbols.Count == 0)
            return [];

        return await DbSet
            .Where(s => symbols.Contains(s.Symbol))
            .OrderBy(s => s.Symbol)
            .ToListAsync(ct);
    }
}
