using SanalBorsa.Domain.Interfaces.Repositories;

namespace SanalBorsa.Domain.Interfaces;

public interface IUnitOfWork : IDisposable
{
    IStockRepository Stocks { get; }

    IStockPriceHistoryRepository PriceHistories { get; }

    ICorporateActionRepository CorporateActions { get; }

    IUserRepository Users { get; }

    IPortfolioRepository Portfolios { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
