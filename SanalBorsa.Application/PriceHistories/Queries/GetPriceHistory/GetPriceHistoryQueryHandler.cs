using AutoMapper;
using MediatR;
using SanalBorsa.Application.Common.Exceptions;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.PriceHistories.Queries.GetPriceHistory;

public class GetPriceHistoryQueryHandler : IRequestHandler<GetPriceHistoryQuery, IReadOnlyList<PriceHistoryDto>>
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;

    public GetPriceHistoryQueryHandler(IUnitOfWork uow, IMapper mapper)
    {
        _uow = uow;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<PriceHistoryDto>> Handle(
        GetPriceHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var stock = await _uow.Stocks.GetBySymbolAsync(request.Symbol.ToUpperInvariant(), cancellationToken)
                    ?? throw new NotFoundException(nameof(Domain.Entities.Stock), request.Symbol);

        var records = await _uow.PriceHistories.GetByStockIdAsync(
            stock.Id,
            request.From,
            request.To,
            cancellationToken);

        return records.Select(_mapper.Map<PriceHistoryDto>).ToList();
    }
}
