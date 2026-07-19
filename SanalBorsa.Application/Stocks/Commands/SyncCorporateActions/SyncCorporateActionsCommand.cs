using MediatR;

namespace SanalBorsa.Application.Stocks.Commands.SyncCorporateActions;

/// <param name="FullResync">
/// When true, imports from İş Yatırım for every stock (fast bootstrap).
/// When false (nightly 23:00), checks KAP for events after the latest DB date and inserts new ones.
/// </param>
/// <param name="Resume">
/// With FullResync: skip wipe and skip stocks that already have at least one corporate action
/// (continue a crashed bootstrap without losing progress).
/// </param>
public record SyncCorporateActionsCommand(bool FullResync = false, bool Resume = false)
    : IRequest<SyncCorporateActionsResult>;

public record SyncCorporateActionsResult(
    int StocksProcessed,
    int StocksSkipped,
    int ActionsAdded,
    int ActionsRemoved,
    int Failed);
