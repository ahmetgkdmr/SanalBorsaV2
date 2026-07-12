using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Domain.Interfaces.Repositories;

public interface IPortfolioRepository : IRepository<UserPortfolio>
{
    Task<UserPortfolio?> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
}
