using Microsoft.EntityFrameworkCore;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces.Repositories;
using SanalBorsa.Infrastructure.Data;

namespace SanalBorsa.Infrastructure.Repositories;

public class PortfolioRepository : BaseRepository<UserPortfolio>, IPortfolioRepository
{
    public PortfolioRepository(AppDbContext context) : base(context) { }

    public async Task<UserPortfolio?> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
        => await DbSet
            .Include(p => p.Holdings)
            .Include(p => p.Transactions)
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);
}
