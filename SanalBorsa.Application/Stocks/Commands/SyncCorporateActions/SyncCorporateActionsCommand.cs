using MediatR;

namespace SanalBorsa.Application.Stocks.Commands.SyncCorporateActions;

/// <param name="FullResync">
/// When true, imports KAP (primary) + İş Yatırım (complementary) merged for every stock.
/// When false (nightly 23:00), checks KAP for events after the latest DB date and inserts new ones.
/// </param>
/// <param name="Resume">
/// With FullResync: skip wipe and skip stocks that already have at least one corporate action
/// (continue a crashed bootstrap without losing progress).
/// </param>
/// <param name="SkipKap">
/// With FullResync: don't call KAP at all, use İş Yatırım only. KAP'ın aralıklı erişilemezliği
/// (response ended prematurely) tam dolguyu saatlerce yavaşlatabiliyor — proje sohbeti: bu bayrakla
/// geçmişi hızlıca İş Yatırım'la doldurup, gece artımlı senkronun (FullResync=false, hep KAP)
/// bundan sonraki olayları günlük olarak KAP'tan yakalamasına güveniyoruz.
/// </param>
public record SyncCorporateActionsCommand(bool FullResync = false, bool Resume = false, bool SkipKap = false)
    : IRequest<SyncCorporateActionsResult>;

public record SyncCorporateActionsResult(
    int StocksProcessed,
    int StocksSkipped,
    int ActionsAdded,
    int ActionsRemoved,
    int Failed,
    IReadOnlyList<string> AffectedSymbols);
