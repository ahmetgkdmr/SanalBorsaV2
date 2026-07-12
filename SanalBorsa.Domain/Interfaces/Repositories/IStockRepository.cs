using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Domain.Interfaces.Repositories;

public interface IStockRepository : IRepository<Stock>
{
    Task<Stock?> GetBySymbolAsync(string symbol, CancellationToken ct = default);

    Task<IReadOnlyList<Stock>> GetAllActiveAsync(CancellationToken ct = default);

    Task<IReadOnlyList<Stock>> GetStocksNeedingRefreshAsync(CancellationToken ct = default);

    Task<bool> ExistsAsync(string symbol, CancellationToken ct = default);

    Task<IReadOnlyList<Stock>> GetBySymbolsAsync(IReadOnlyList<string> symbols, CancellationToken ct = default);
}
