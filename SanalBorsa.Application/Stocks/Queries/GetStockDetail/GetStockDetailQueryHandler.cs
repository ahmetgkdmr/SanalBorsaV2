using AutoMapper;
using MediatR;
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
        var stock = await _uow.Stocks.GetBySymbolAsync(request.Symbol.ToUpperInvariant(), cancellationToken)
                    ?? throw new NotFoundException(nameof(Domain.Entities.Stock), request.Symbol);

        var recentPrices = await _uow.PriceHistories.GetByStockIdAsync(
            stock.Id,
            from: DateTime.UtcNow.AddDays(-30),
            ct: cancellationToken);

        var actions = await _uow.CorporateActions.GetByStockIdAsync(stock.Id, cancellationToken);

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
            stock.EarliestDataDate,
            stock.LatestDataDate,
            stock.NeedsHistoryRefresh,
            stock.CreatedAt,
            stock.UpdatedAt,
            priceDtos,
            actionDtos);
    }
}
