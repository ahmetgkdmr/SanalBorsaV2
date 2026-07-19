using MediatR;

namespace SanalBorsa.Application.Stocks.Commands.WipeAllPriceHistories;

public record WipeAllPriceHistoriesCommand() : IRequest<WipeAllPriceHistoriesResult>;

public record WipeAllPriceHistoriesResult(int DeletedRows);
