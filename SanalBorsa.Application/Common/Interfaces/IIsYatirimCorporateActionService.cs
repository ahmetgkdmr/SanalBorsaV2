using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Application.Common.Interfaces;

/// <summary>
/// Fetches BIST corporate actions (bedelli, bedelsiz, nakit temettü) from İş Yatırım
/// "Sermaye Artırımları / Temettüler" company-card data.
/// </summary>
public interface IIsYatirimCorporateActionService
{
    Task<IReadOnlyList<CorporateAction>> GetCorporateActionsAsync(
        string bistSymbol,
        CancellationToken ct = default);
}
