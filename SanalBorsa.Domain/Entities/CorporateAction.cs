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
    /// - Dividend: cash amount per share in TRY
    /// - BonusIssue: bonus ratio (e.g. 0.5 means 1 new share per 2 existing)
    /// - RightsIssue: rights ratio
    /// </summary>
    public decimal Value { get; set; }

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }
}
