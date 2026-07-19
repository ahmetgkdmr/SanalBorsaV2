using Microsoft.EntityFrameworkCore;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces.Repositories;
using SanalBorsa.Infrastructure.Data;

namespace SanalBorsa.Infrastructure.Repositories;

public class TopGainerRepository : BaseRepository<TopGainer>, ITopGainerRepository
{
    public TopGainerRepository(AppDbContext context) : base(context) { }

    public new async Task<IReadOnlyList<TopGainer>> GetAllAsync(CancellationToken ct = default)
        => await DbSet
            .AsNoTracking()
            .OrderBy(t => t.Period)
            .ThenBy(t => t.Rank)
            .ToListAsync(ct);

    public async Task ReplaceAllAsync(IReadOnlyList<TopGainer> rows, CancellationToken ct = default)
    {
        await DbSet.ExecuteDeleteAsync(ct);
        if (rows.Count == 0) return;
        await DbSet.AddRangeAsync(rows, ct);
    }
}
