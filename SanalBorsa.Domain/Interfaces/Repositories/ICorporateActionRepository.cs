using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Enums;

namespace SanalBorsa.Domain.Interfaces.Repositories;

public interface ICorporateActionRepository : IRepository<CorporateAction>
{
    Task<IReadOnlyList<CorporateAction>> GetByStockIdAsync(int stockId, CancellationToken ct = default);

    Task<bool> ExistsAsync(int stockId, DateTime date, CorporateActionType type, CancellationToken ct = default);

    Task<IReadOnlyList<CorporateAction>> GetByStockIdAndTypeAsync(
        int stockId,
        CorporateActionType type,
        CancellationToken ct = default);
}
