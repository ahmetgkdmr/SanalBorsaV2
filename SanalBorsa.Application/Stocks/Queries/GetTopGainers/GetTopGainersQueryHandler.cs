using MediatR;
using SanalBorsa.Application.Common;
using SanalBorsa.Application.Common.Seeds;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Stocks.Queries.GetTopGainers;

public class GetTopGainersQueryHandler : IRequestHandler<GetTopGainersQuery, TopGainersResponseDto>
{
    private readonly IUnitOfWork _uow;

    public GetTopGainersQueryHandler(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<TopGainersResponseDto> Handle(
        GetTopGainersQuery request,
        CancellationToken cancellationToken)
    {
        var rows = await _uow.TopGainers.GetByMarketAsync(request.MarketType, cancellationToken);
        if (rows.Count == 0)
            return new TopGainersResponseDto(null, null, []);

        var stockIds = rows.Select(r => r.StockId).Distinct().ToList();
        var snapshots = await _uow.PriceHistories.GetMarketSnapshotsAsync(
            stockIds, sparklineDays: 28, cancellationToken);

        var items = rows
            .OrderBy(r => TopGainerPeriodInfo.SortOrder(r.Period))
            .ThenBy(r => r.Rank)
            .Select(r =>
            {
                snapshots.TryGetValue(r.StockId, out var snap);
                return new TopGainerDto(
                    Period: TopGainerPeriodInfo.Key(r.Period),
                    PeriodLabel: TopGainerPeriodInfo.Label(r.Period),
                    PeriodShortLabel: TopGainerPeriodInfo.ShortLabel(r.Period),
                    Rank: r.Rank,
                    Symbol: r.Symbol,
                    Name: r.Name,
                    ReturnPct: r.ReturnPct,
                    StartPrice: r.StartPrice,
                    EndPrice: r.EndPrice,
                    StartDate: r.StartDate,
                    EndDate: r.EndDate,
                    LastClose: snap?.LastClose,
                    PreviousClose: snap?.PreviousClose,
                    Sparkline: snap?.Sparkline,
                    BistIndices: request.MarketType == MarketType.Bist
                        ? BistIndexCompositionSeed.GetIndicesForSymbol(r.Symbol)
                        : null);
            })
            .ToList();

        return new TopGainersResponseDto(
            rows.Max(r => r.EndDate),
            rows.Max(r => r.ComputedAt),
            items);
    }
}
