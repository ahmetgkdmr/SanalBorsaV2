using Microsoft.EntityFrameworkCore;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Enums;
using SanalBorsa.Domain.Interfaces.Repositories;
using SanalBorsa.Infrastructure.Data;

namespace SanalBorsa.Infrastructure.Repositories;

public class TopGainerRepository : BaseRepository<TopGainer>, ITopGainerRepository
{
    public TopGainerRepository(AppDbContext context) : base(context) { }

    public new async Task<IReadOnlyList<TopGainer>> GetAllAsync(CancellationToken ct = default)
        => await DbSet
            .AsNoTracking()
            .OrderBy(t => t.MarketType)
            .ThenBy(t => t.Period)
            .ThenBy(t => t.Rank)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TopGainer>> GetByMarketAsync(
        MarketType marketType,
        CancellationToken ct = default)
        => await DbSet
            .AsNoTracking()
            .Where(t => t.MarketType == marketType)
            .OrderBy(t => t.Period)
            .ThenBy(t => t.Rank)
            .ToListAsync(ct);

    public async Task ReplaceForMarketAsync(
        MarketType marketType,
        IReadOnlyList<TopGainer> rows,
        CancellationToken ct = default)
    {
        await DbSet.Where(t => t.MarketType == marketType).ExecuteDeleteAsync(ct);
        if (rows.Count == 0) return;
        await DbSet.AddRangeAsync(rows, ct);
    }
}
