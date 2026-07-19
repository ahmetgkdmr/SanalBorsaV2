using SanalBorsa.Domain.Interfaces.Repositories;

namespace SanalBorsa.Domain.Interfaces;

public interface IUnitOfWork : IDisposable
{
    IStockRepository Stocks { get; }

    IStockPriceHistoryRepository PriceHistories { get; }

    ICorporateActionRepository CorporateActions { get; }

    IUserRepository Users { get; }

    IPortfolioRepository Portfolios { get; }

    ITopGainerRepository TopGainers { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Detaches all tracked entities after a failed SaveChanges so the next stock can proceed.</summary>
    void ClearChanges();
}
