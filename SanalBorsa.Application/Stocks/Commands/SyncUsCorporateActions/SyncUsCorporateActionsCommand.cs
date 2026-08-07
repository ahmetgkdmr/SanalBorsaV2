using MediatR;

namespace SanalBorsa.Application.Stocks.Commands.SyncUsCorporateActions;

/// <summary>
/// ABD hisseleri için temettü + split senkronu — tek kaynak (Yahoo Finance), bu yüzden BIST'in
/// KAP/İş Yatırım FullResync/Resume ayrımına gerek yok. Pilotta wipe yapılmaz, sadece ekle/dedupe.
/// </summary>
public record SyncUsCorporateActionsCommand(string? Symbol = null)
    : IRequest<SyncUsCorporateActionsResult>;

public record SyncUsCorporateActionsResult(
    int StocksProcessed,
    int StocksSkipped,
    int ActionsAdded,
    int Failed,
    IReadOnlyList<string> AffectedSymbols);
