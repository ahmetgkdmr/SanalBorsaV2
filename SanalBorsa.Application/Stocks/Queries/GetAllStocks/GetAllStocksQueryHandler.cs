using AutoMapper;
using MediatR;
using SanalBorsa.Application.Common.Models;
using SanalBorsa.Application.Common.Seeds;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Enums;
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
        var topGainers = await _uow.TopGainers.GetAllAsync(cancellationToken);
        var championBySymbol = topGainers
            .GroupBy(t => t.Symbol)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Period).First());

        IEnumerable<Stock> filtered = all.Where(s => !MarketInstrumentSeed.IsMarketInstrument(s.Exchange));

        if (request.IsActive.HasValue)
            filtered = filtered.Where(s => s.IsActive == request.IsActive.Value);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToUpperInvariant();
            filtered = filtered.Where(s =>
                s.Symbol.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                s.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var hasIndexFilter = !string.IsNullOrWhiteSpace(request.IndexFilter) &&
            !request.IndexFilter.Equals("all", StringComparison.OrdinalIgnoreCase);

        if (hasIndexFilter)
        {
            filtered = filtered.Where(s =>
                BistIndexCompositionSeed.SymbolMatchesFilter(s.Symbol, request.IndexFilter!));
        }

        // Kategoride olan şampiyonlar en başta; filtrede olmayanlar hiç gelmez
        var ordered = filtered
            .OrderBy(s => championBySymbol.TryGetValue(s.Symbol, out var c)
                ? c.Period switch
                {
                    TopGainerPeriod.Week => 0,
                    TopGainerPeriod.Month => 1,
                    TopGainerPeriod.Year => 2,
                    _ => 9,
                }
                : 9)
            .ThenBy(s => s.Symbol)
            .ToList();
        var total = ordered.Count;
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize < 1 ? 50 : Math.Min(request.PageSize, 500);

        var pageItems = ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var snapshots = await _uow.PriceHistories.GetMarketSnapshotsAsync(
            pageItems.Select(s => s.Id).ToList(),
            sparklineDays: 28,
            cancellationToken);

        var items = pageItems
            .Select(stock =>
            {
                var dto = _mapper.Map<StockDto>(stock);
                var bistIndices = BistIndexCompositionSeed.GetIndicesForSymbol(stock.Symbol);
                championBySymbol.TryGetValue(stock.Symbol, out var crown);

                string? period = null;
                string? label = null;
                decimal? ret = null;
                if (crown is not null)
                {
                    period = crown.Period switch
                    {
                        TopGainerPeriod.Week => "week",
                        TopGainerPeriod.Month => "month",
                        TopGainerPeriod.Year => "year",
                        _ => null,
                    };
                    label = crown.Period switch
                    {
                        TopGainerPeriod.Week => "Son 1 haftanın en çok kazananı",
                        TopGainerPeriod.Month => "Son 1 ayın en çok kazananı",
                        TopGainerPeriod.Year => "Son 1 yılın en çok kazananı",
                        _ => null,
                    };
                    ret = crown.ReturnPct;
                }

                if (!snapshots.TryGetValue(stock.Id, out var snap))
                {
                    return dto with
                    {
                        BistIndices = bistIndices,
                        TopGainerPeriod = period,
                        TopGainerLabel = label,
                        TopGainerReturnPct = ret,
                    };
                }

                return dto with
                {
                    LastClose = snap.LastClose,
                    LastOpen = snap.LastOpen,
                    PreviousClose = snap.PreviousClose,
                    LastVolume = snap.LastVolume,
                    Sparkline = snap.Sparkline,
                    BistIndices = bistIndices,
                    TopGainerPeriod = period,
                    TopGainerLabel = label,
                    TopGainerReturnPct = ret,
                };
            })
            .ToList();

        return new PagedResult<StockDto>(items, total, page, pageSize);
    }
}
