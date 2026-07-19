using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Application.Common.Interfaces;

/// <summary>
/// Fetches corporate actions from KAP public disclosure APIs (no paid subscription).
/// Parses Kar Payı + Sermaye Artırımı notification bodies into typed actions
/// (BonusIssue, RightsIssue + SubscriptionPrice, Dividend).
/// </summary>
public interface IKapCorporateActionService
{
    /// <param name="sinceDate">
    /// When set, only fetches disclosure years from that year onward and returns
    /// actions with ActionDate on/after that date (incremental nightly path).
    /// </param>
    Task<IReadOnlyList<CorporateAction>> GetCorporateActionsAsync(
        string bistSymbol,
        DateTime? sinceDate = null,
        CancellationToken ct = default);
}
