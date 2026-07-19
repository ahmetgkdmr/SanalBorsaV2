using SanalBorsa.Domain.Enums;

namespace SanalBorsa.Domain.Entities;

public class CorporateAction
{
    public int Id { get; set; }

    public int StockId { get; set; }

    public Stock Stock { get; set; } = null!;

    public CorporateActionType ActionType { get; set; }

    public DateTime ActionDate { get; set; }

    /// <summary>
    /// Interpretation depends on ActionType:
    /// - Dividend: cash amount per share in TRY (Hisse Başı Brüt)
    /// - BonusIssue: total lot multiplier after the event (e.g. %100 bedelsiz → 2.0, %15 → 1.15)
    /// - RightsIssue: new/old ratio (e.g. %100 bedelli → 1.0 → lots doubles via lots += lots * value)
    /// </summary>
    public decimal Value { get; set; }

    /// <summary>
    /// RightsIssue only: rüçhan hakkı kullandırma fiyatı (TL / yeni hisse).
    /// Null for BonusIssue / Dividend.
    /// </summary>
    public decimal? SubscriptionPrice { get; set; }

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }
}
