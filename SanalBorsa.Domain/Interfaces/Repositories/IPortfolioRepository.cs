using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Domain.Interfaces.Repositories;

public interface IPortfolioRepository : IRepository<UserPortfolio>
{
    /// <summary>Portföy + holdings (işlem geçmişi dahil değil).</summary>
    Task<UserPortfolio?> GetByUserIdAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// İşlem geçmişi sayfalı. En yeni önce.
    /// </summary>
    Task<(IReadOnlyList<PortfolioTransaction> Items, int TotalCount)> GetTransactionsPagedAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken ct = default);
}
