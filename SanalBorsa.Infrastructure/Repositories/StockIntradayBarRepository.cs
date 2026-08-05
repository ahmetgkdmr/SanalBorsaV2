using Microsoft.EntityFrameworkCore;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces.Repositories;
using SanalBorsa.Infrastructure.Data;

namespace SanalBorsa.Infrastructure.Repositories;

public class StockIntradayBarRepository : IStockIntradayBarRepository
{
    private readonly AppDbContext _context;

    public StockIntradayBarRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task DeleteAllByMarketAsync(MarketType market, CancellationToken ct = default)
    {
        await _context.StockIntradayBars
            .Where(b => _context.Stocks
                .Where(s => s.MarketType == market)
                .Select(s => s.Id)
                .Contains(b.StockId))
            .ExecuteDeleteAsync(ct);
    }

    public async Task BulkInsertAsync(IEnumerable<StockIntradayBar> bars, CancellationToken ct = default)
    {
        const int chunkSize = 2000;
        foreach (var chunk in bars.Chunk(chunkSize))
        {
            await _context.StockIntradayBars.AddRangeAsync(chunk, ct);
            await _context.SaveChangesAsync(ct);
            _context.ChangeTracker.Clear();
        }
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<decimal>>> GetSparklinesByStockIdsAsync(
        IReadOnlyList<int> stockIds,
        CancellationToken ct = default)
    {
        if (stockIds.Count == 0)
            return new Dictionary<int, IReadOnlyList<decimal>>();

        var rows = await _context.StockIntradayBars
            .Where(b => stockIds.Contains(b.StockId))
            .OrderBy(b => b.StockId).ThenBy(b => b.BarTime)
            .Select(b => new { b.StockId, b.Close })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.StockId)
            .ToDictionary(
                g => g.Key,
                IReadOnlyList<decimal> (g) => g.Select(r => r.Close).ToList());
    }
}
