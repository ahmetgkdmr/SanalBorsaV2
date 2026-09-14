using MediatR;
using SanalBorsa.Application.Common.Exceptions;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Application.Mappings;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.CorporateActions.Queries.GetCorporateActions;

public class GetCorporateActionsQueryHandler
    : IRequestHandler<GetCorporateActionsQuery, IReadOnlyList<CorporateActionDto>>
{
    private readonly IUnitOfWork _uow;

    public GetCorporateActionsQueryHandler(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<IReadOnlyList<CorporateActionDto>> Handle(
        GetCorporateActionsQuery request,
        CancellationToken cancellationToken)
    {
        var stock = await _uow.Stocks.GetBySymbolAsync(request.Symbol.ToUpperInvariant(), cancellationToken)
                    ?? throw new NotFoundException(nameof(Domain.Entities.Stock), request.Symbol);

        var actions = request.ActionType.HasValue
            ? await _uow.CorporateActions.GetByStockIdAndTypeAsync(
                stock.Id, request.ActionType.Value, cancellationToken)
            : await _uow.CorporateActions.GetByStockIdAsync(stock.Id, cancellationToken);

        return actions
            .Select(a => a.ToDto() with { Symbol = stock.Symbol })
            .ToList();
    }
}
