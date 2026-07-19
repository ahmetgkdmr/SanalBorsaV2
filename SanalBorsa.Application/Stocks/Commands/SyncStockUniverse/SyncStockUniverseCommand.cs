using MediatR;

namespace SanalBorsa.Application.Stocks.Commands.SyncStockUniverse;

public record UniverseStockDto(string Symbol, string Name);

public record SyncStockUniverseCommand(
    IReadOnlyList<UniverseStockDto> Add,
    IReadOnlyList<string> Remove)
    : IRequest<SyncStockUniverseResult>;

public record SyncStockUniverseResult(
    int Added,
    int Removed,
    int SkippedExisting,
    int SkippedMissing,
    IReadOnlyList<string> AddedSymbols,
    IReadOnlyList<string> RemovedSymbols);
