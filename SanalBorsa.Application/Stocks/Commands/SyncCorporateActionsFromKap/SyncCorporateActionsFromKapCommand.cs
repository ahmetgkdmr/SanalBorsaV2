using MediatR;

namespace SanalBorsa.Application.Stocks.Commands.SyncCorporateActionsFromKap;

public record SyncCorporateActionsFromKapCommand(string Symbol)
    : IRequest<SyncCorporateActionsFromKapResult>;

public record SyncCorporateActionsFromKapResult(
    string Symbol,
    int ActionsRemoved,
    int ActionsAdded,
    IReadOnlyList<string> Preview);
