using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Enums;

namespace SanalBorsa.Domain.Interfaces.Repositories;

public interface ICorporateActionRepository : IRepository<CorporateAction>
{
    Task<IReadOnlyList<CorporateAction>> GetByStockIdAsync(int stockId, CancellationToken ct = default);

    Task<bool> ExistsAsync(int stockId, DateTime date, CorporateActionType type, CancellationToken ct = default);

    Task<DateTime?> GetLatestActionDateAsync(int stockId, CancellationToken ct = default);

    Task<int> DeleteAllByStockIdAsync(int stockId, CancellationToken ct = default);

    /// <summary>Wipes the entire CorporateActions table (e.g. before full KAP re-import).</summary>
    Task<int> DeleteAllAsync(CancellationToken ct = default);

    Task<IReadOnlyList<CorporateAction>> GetByStockIdAndTypeAsync(
        int stockId,
        CorporateActionType type,
        CancellationToken ct = default);

    /// <summary>Birden fazla hisse için aksiyonlar (ActionDate artan).</summary>
    Task<IReadOnlyList<CorporateAction>> GetByStockIdsAsync(
        IReadOnlyList<int> stockIds,
        CancellationToken ct = default);

    /// <summary>
    /// Portföylere henüz uygulanmamış (AppliedToPortfolios=false), tarihi geçmiş/bugün olan
    /// Bedelsiz/Bedelli/Temettü olayları — Stock dahil (Symbol/MarketType için).
    /// </summary>
    Task<IReadOnlyList<CorporateAction>> GetUnappliedPortfolioActionsAsync(
        DateTime onOrBeforeDate,
        CancellationToken ct = default);

    /// <summary>Bu (olay, portföy) çifti daha önce işlendi mi — idempotency kontrolü.</summary>
    Task<bool> IsAppliedToPortfolioAsync(int corporateActionId, Guid portfolioId, CancellationToken ct = default);

    /// <summary>Bir (olay, portföy) uygulamasını kalıcı olarak işaretler.</summary>
    Task RecordAppliedAsync(
        int corporateActionId, Guid portfolioId, string effect, CancellationToken ct = default);
}
