using MediatR;
using SanalBorsa.Application.Common.Seeds;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Enums;
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
        var rows = await _uow.TopGainers.GetAllAsync(cancellationToken);
        if (rows.Count == 0)
            return new TopGainersResponseDto(null, null, []);

        var stockIds = rows.Select(r => r.StockId).Distinct().ToList();
        var snapshots = await _uow.PriceHistories.GetMarketSnapshotsAsync(
            stockIds, sparklineDays: 28, cancellationToken);

        var items = rows
            .OrderBy(r => r.Period)
            .ThenBy(r => r.Rank)
            .Select(r =>
            {
                snapshots.TryGetValue(r.StockId, out var snap);
                return new TopGainerDto(
                    Period: PeriodKey(r.Period),
                    PeriodLabel: PeriodLabel(r.Period),
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
                    BistIndices: BistIndexCompositionSeed.GetIndicesForSymbol(r.Symbol));
            })
            .ToList();

        return new TopGainersResponseDto(
            rows.Max(r => r.EndDate),
            rows.Max(r => r.ComputedAt),
            items);
    }

    private static string PeriodKey(TopGainerPeriod p) => p switch
    {
        TopGainerPeriod.Week => "week",
        TopGainerPeriod.Month => "month",
        TopGainerPeriod.Year => "year",
        _ => p.ToString().ToLowerInvariant(),
    };

    private static string PeriodLabel(TopGainerPeriod p) => p switch
    {
        TopGainerPeriod.Week => "Son 1 haftanın en çok kazananı",
        TopGainerPeriod.Month => "Son 1 ayın en çok kazananı",
        TopGainerPeriod.Year => "Son 1 yılın en çok kazananı",
        _ => "En çok kazanan",
    };
}
