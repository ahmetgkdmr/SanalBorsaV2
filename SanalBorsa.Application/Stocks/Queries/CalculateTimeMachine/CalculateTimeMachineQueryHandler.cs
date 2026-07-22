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
        if (request.MarketType == MarketType.Crypto && !symbol.EndsWith("USDT", StringComparison.Ordinal))
            symbol += "USDT";

        var stock = await _uow.Stocks.GetBySymbolAsync(symbol, cancellationToken, request.MarketType)
                    ?? throw new NotFoundException(nameof(Domain.Entities.Stock), symbol);

        var prices = await _uow.PriceHistories.GetByStockIdAsync(
            stock.Id,
            from: request.Date.Date,
            ct: cancellationToken);

        // Crypto ve endeks: corp action yok
        IReadOnlyList<CorporateAction> actions =
            request.MarketType == MarketType.Crypto
            || MarketInstrumentSeed.IsMarketInstrument(stock.Exchange)
                ? []
                : await _uow.CorporateActions.GetByStockIdAsync(stock.Id, cancellationToken);

        return TimeMachineCalculator.Calculate(
            stock.Symbol,
            prices,
            actions,
            request.Date.Date,
            request.WagePercentage,
            request.Mode,
            request.Amount);
    }
}
