using AutoMapper;
using MediatR;
using SanalBorsa.Application.Common.Models;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Stocks.Queries.GetUsStocks;

public class GetUsStocksQueryHandler : IRequestHandler<GetUsStocksQuery, PagedResult<StockDto>>
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;

    public GetUsStocksQueryHandler(IUnitOfWork uow, IMapper mapper)
    {
        _uow = uow;
        _mapper = mapper;
    }

    public async Task<PagedResult<StockDto>> Handle(GetUsStocksQuery request, CancellationToken cancellationToken)
    {
        var all = (await _uow.Stocks.GetAllAsync(cancellationToken))
            .Where(s => s.MarketType == MarketType.UsStocks)
            .ToList();

        if (request.IsActive.HasValue)
            all = all.Where(s => s.IsActive == request.IsActive.Value).ToList();

        var ordered = all.OrderBy(s => s.Symbol, StringComparer.OrdinalIgnoreCase).ToList();

        var snapshots = await _uow.PriceHistories.GetMarketSnapshotsAsync(
            ordered.Select(s => s.Id).ToList(),
            sparklineDays: 28,
            cancellationToken);

        var items = ordered
            .Select(stock =>
            {
                var dto = _mapper.Map<StockDto>(stock) with { MarketType = "us" };
                if (!snapshots.TryGetValue(stock.Id, out var snap))
                    return dto;

                return dto with
                {
                    LastClose = snap.LastClose,
                    LastOpen = snap.LastOpen,
                    PreviousClose = snap.PreviousClose,
                    LastVolume = snap.LastVolume,
                    Sparkline = snap.Sparkline,
                };
            })
            .ToList();

        return new PagedResult<StockDto>(items, items.Count, 1, Math.Max(items.Count, 1));
    }
}
