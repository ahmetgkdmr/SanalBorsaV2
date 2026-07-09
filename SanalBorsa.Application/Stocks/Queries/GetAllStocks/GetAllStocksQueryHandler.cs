using AutoMapper;
using MediatR;
using SanalBorsa.Application.Common.Models;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Stocks.Queries.GetAllStocks;

public class GetAllStocksQueryHandler : IRequestHandler<GetAllStocksQuery, PagedResult<StockDto>>
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;

    public GetAllStocksQueryHandler(IUnitOfWork uow, IMapper mapper)
    {
        _uow = uow;
        _mapper = mapper;
    }

    public async Task<PagedResult<StockDto>> Handle(GetAllStocksQuery request, CancellationToken cancellationToken)
    {
        var all = await _uow.Stocks.GetAllAsync(cancellationToken);

        IEnumerable<Stock> filtered = all;

        if (request.IsActive.HasValue)
            filtered = filtered.Where(s => s.IsActive == request.IsActive.Value);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToUpperInvariant();
            filtered = filtered.Where(s =>
                s.Symbol.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                s.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var ordered = filtered.OrderBy(s => s.Symbol).ToList();
        var total = ordered.Count;
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize < 1 ? 50 : Math.Min(request.PageSize, 500);

        var items = ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(_mapper.Map<StockDto>)
            .ToList();

        return new PagedResult<StockDto>(items, total, page, pageSize);
    }
}
