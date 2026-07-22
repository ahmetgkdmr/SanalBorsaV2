using Microsoft.EntityFrameworkCore;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces.Repositories;
using SanalBorsa.Infrastructure.Data;

namespace SanalBorsa.Infrastructure.Repositories;

public class StockRepository : BaseRepository<Stock>, IStockRepository
{
    public StockRepository(AppDbContext context) : base(context) { }

    public async Task<Stock?> GetBySymbolAsync(
        string symbol,
        CancellationToken ct = default,
        MarketType marketType = MarketType.Bist)
        => await DbSet
            .FirstOrDefaultAsync(s => s.Symbol == symbol && s.MarketType == marketType, ct);

    public async Task<IReadOnlyList<Stock>> GetAllActiveAsync(
        CancellationToken ct = default,
        MarketType? marketType = MarketType.Bist)
    {
        var q = DbSet.AsQueryable().Where(s => s.IsActive);
        if (marketType.HasValue)
            q = q.Where(s => s.MarketType == marketType.Value);
        return await q.OrderBy(s => s.Symbol).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Stock>> GetStocksNeedingRefreshAsync(
        CancellationToken ct = default,
        MarketType marketType = MarketType.Bist)
        => await DbSet
            .Where(s =>
                s.IsActive
                && s.MarketType == marketType
                && (s.NeedsHistoryRefresh || s.EarliestDataDate == null))
            .OrderBy(s => s.Symbol)
            .ToListAsync(ct);

    public async Task<bool> ExistsAsync(
        string symbol,
        CancellationToken ct = default,
        MarketType marketType = MarketType.Bist)
        => await DbSet.AnyAsync(s => s.Symbol == symbol && s.MarketType == marketType, ct);

    public async Task<IReadOnlyList<Stock>> GetBySymbolsAsync(
        IReadOnlyList<string> symbols,
        CancellationToken ct = default,
        MarketType marketType = MarketType.Bist)
    {
        if (symbols.Count == 0)
            return [];

        return await DbSet
            .Where(s => s.MarketType == marketType && symbols.Contains(s.Symbol))
            .OrderBy(s => s.Symbol)
            .ToListAsync(ct);
    }
}
