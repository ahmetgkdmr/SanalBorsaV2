using MediatR;

namespace SanalBorsa.Application.Indices.Commands.BootstrapMarketIndices;

public record BootstrapMarketIndicesCommand : IRequest<BootstrapMarketIndicesResult>;

public record BootstrapMarketIndicesResult(
    int InstrumentsAdded,
    int InstrumentsProcessed,
    int PriceRecordsInserted,
    int Failed);
