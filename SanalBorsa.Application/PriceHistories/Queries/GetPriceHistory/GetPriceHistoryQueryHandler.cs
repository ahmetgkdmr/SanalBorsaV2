using MediatR;
using SanalBorsa.Application.Common.Exceptions;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Application.Mappings;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.PriceHistories.Queries.GetPriceHistory;

public class GetPriceHistoryQueryHandler : IRequestHandler<GetPriceHistoryQuery, IReadOnlyList<PriceHistoryDto>>
{
    private readonly IUnitOfWork _uow;

    public GetPriceHistoryQueryHandler(IUnitOfWork uow)
    {
        _uow = uow;
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

        return records.Select(x => x.ToDto()).ToList();
    }
}
