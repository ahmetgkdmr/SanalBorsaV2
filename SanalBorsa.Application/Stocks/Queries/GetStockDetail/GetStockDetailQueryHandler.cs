using AutoMapper;
using MediatR;
using SanalBorsa.Application.Common;
using SanalBorsa.Application.Common.Exceptions;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Stocks.Queries.GetStockDetail;

public class GetStockDetailQueryHandler : IRequestHandler<GetStockDetailQuery, StockDetailDto>
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;

    public GetStockDetailQueryHandler(IUnitOfWork uow, IMapper mapper)
    {
        _uow = uow;
        _mapper = mapper;
    }

    public async Task<StockDetailDto> Handle(GetStockDetailQuery request, CancellationToken cancellationToken)
    {
        var stock = await _uow.Stocks.GetBySymbolAsync(
                        request.Symbol.ToUpperInvariant(), cancellationToken, request.MarketType)
                    ?? throw new NotFoundException(nameof(Domain.Entities.Stock), request.Symbol);

        var recentPrices = await _uow.PriceHistories.GetByStockIdAsync(
            stock.Id,
            from: DateTime.UtcNow.AddDays(-30),
            ct: cancellationToken);

        var actions = await _uow.CorporateActions.GetByStockIdAsync(stock.Id, cancellationToken);

        var usdTry = await _uow.Stocks.GetBySymbolAsync("USDTRY", cancellationToken);
        var earliestDataDate = EarliestDateClamp.Apply(stock.EarliestDataDate, usdTry?.EarliestDataDate);

        var priceDtos = recentPrices
            .Select(_mapper.Map<PriceHistoryDto>)
            .ToList();

        var actionDtos = actions
            .Select(a =>
            {
                var dto = _mapper.Map<CorporateActionDto>(a);
                // Inject stock symbol since lazy loading is not used
                return dto with { Symbol = stock.Symbol };
            })
            .ToList();

        // recentPrices tarihe göre artan; son iki gün → close / previousClose / volume
        var latest = recentPrices.Count > 0 ? recentPrices[^1] : null;
        var previous = recentPrices.Count > 1 ? recentPrices[^2] : null;
        var sparkline = recentPrices
            .TakeLast(28)
            .Select(p => p.Close)
            .ToList();

        return new StockDetailDto(
            stock.Id,
            stock.Symbol,
            stock.YahooSymbol,
            stock.Name,
            stock.Sector,
            stock.Industry,
            stock.Currency,
            stock.Exchange,
            stock.IsActive,
            earliestDataDate,
            stock.LatestDataDate,
            stock.NeedsHistoryRefresh,
            stock.CreatedAt,
            stock.UpdatedAt,
            priceDtos,
            actionDtos,
            LastClose: latest?.Close,
            LastOpen: latest?.Open,
            PreviousClose: previous?.Close,
            LastVolume: latest?.Volume,
            Sparkline: sparkline);
    }
}
