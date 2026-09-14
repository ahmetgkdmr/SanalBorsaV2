using Microsoft.EntityFrameworkCore;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces.Repositories;
using SanalBorsa.Infrastructure.Data;

namespace SanalBorsa.Infrastructure.Repositories;

public class PortfolioRepository : BaseRepository<UserPortfolio>, IPortfolioRepository
{
    public PortfolioRepository(AppDbContext context) : base(context) { }

    public async Task<IReadOnlyList<UserPortfolio>> GetAllWithUserAndHoldingsAsync(CancellationToken ct = default)
        => await DbSet
            .AsNoTracking()
            .Include(p => p.User)
            .Include(p => p.Holdings)
            .AsSplitQuery()
            .ToListAsync(ct);

    public async Task<UserPortfolio?> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
        => await DbSet
            .Include(p => p.Holdings)
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

    public async Task<UserPortfolio?> GetByIdWithHoldingsAsync(Guid portfolioId, CancellationToken ct = default)
        => await DbSet
            .Include(p => p.Holdings)
            .FirstOrDefaultAsync(p => p.Id == portfolioId, ct);

    public async Task<IReadOnlyList<Guid>> GetPortfolioIdsWithSymbolHistoryAsync(
        string symbol,
        MarketType market,
        CancellationToken ct = default)
        => await Context.PortfolioTransactions
            .AsNoTracking()
            .Where(t => t.Symbol == symbol && t.MarketType == market)
            .Select(t => t.PortfolioId)
            .Distinct()
            .ToListAsync(ct);

    public async Task<decimal> GetNetQuantityAsOfAsync(
        Guid portfolioId,
        string symbol,
        MarketType market,
        DateTime cutoffUtc,
        CancellationToken ct = default)
    {
        var rows = await Context.PortfolioTransactions
            .AsNoTracking()
            .Where(t => t.PortfolioId == portfolioId
                && t.Symbol == symbol
                && t.MarketType == market
                && t.ExecutedAt <= cutoffUtc)
            .Select(t => new { t.Side, t.Quantity })
            .ToListAsync(ct);

        return rows.Sum(t => t.Side == TxSide.Buy ? t.Quantity : -t.Quantity);
    }

    public async Task<(IReadOnlyList<PortfolioTransaction> Items, int TotalCount)> GetTransactionsPagedAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var portfolioId = await DbSet
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct);

        if (portfolioId is null)
            return (Array.Empty<PortfolioTransaction>(), 0);

        var query = Context.PortfolioTransactions
            .AsNoTracking()
            .Where(t => t.PortfolioId == portfolioId.Value);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(t => t.ExecutedAt)
            .ThenByDescending(t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }
}
