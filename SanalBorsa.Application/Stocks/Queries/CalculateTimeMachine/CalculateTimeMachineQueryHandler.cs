using MediatR;
using SanalBorsa.Application.Common.Exceptions;
using SanalBorsa.Application.Common.Seeds;
using SanalBorsa.Application.Common.Services;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Stocks.Queries.CalculateTimeMachine;

public class CalculateTimeMachineQueryHandler : IRequestHandler<CalculateTimeMachineQuery, TimeMachineResultDto>
{
    private readonly IUnitOfWork _uow;

    public CalculateTimeMachineQueryHandler(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<TimeMachineResultDto> Handle(
        CalculateTimeMachineQuery request,
        CancellationToken cancellationToken)
    {
        var symbol = request.Symbol.ToUpperInvariant();
        var stock = await _uow.Stocks.GetBySymbolAsync(symbol, cancellationToken)
                    ?? throw new NotFoundException(nameof(Domain.Entities.Stock), symbol);

        var prices = await _uow.PriceHistories.GetByStockIdAsync(
            stock.Id,
            from: request.Date.Date,
            ct: cancellationToken);

        IReadOnlyList<CorporateAction> actions = MarketInstrumentSeed.IsMarketInstrument(stock.Exchange)
            ? []
            : await _uow.CorporateActions.GetByStockIdAsync(stock.Id, cancellationToken);

        return TimeMachineCalculator.Calculate(
            stock.Symbol,
            prices,
            actions,
            request.Date.Date,
            request.WagePercentage,
            request.Mode);
    }
}
