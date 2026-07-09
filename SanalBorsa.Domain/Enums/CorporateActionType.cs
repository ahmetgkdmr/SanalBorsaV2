namespace SanalBorsa.Domain.Enums;

public enum CorporateActionType
{
    /// <summary>Bedelli sermaye artırımı — rights issue where shareholders pay to acquire new shares</summary>
    RightsIssue = 1,

    /// <summary>Bedelsiz sermaye artırımı — bonus share issue at no cost to shareholders (stock split variant)</summary>
    BonusIssue = 2,

    /// <summary>Temettü — cash dividend distribution</summary>
    Dividend = 3
}
