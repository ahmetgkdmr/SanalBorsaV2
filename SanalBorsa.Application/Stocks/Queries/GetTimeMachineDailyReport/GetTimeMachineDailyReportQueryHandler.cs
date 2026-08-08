using MediatR;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Enums;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Stocks.Queries.GetTimeMachineDailyReport;

public class GetTimeMachineDailyReportQueryHandler
    : IRequestHandler<GetTimeMachineDailyReportQuery, TimeMachineDailyReportDto>
{
    private const int TopCount = 3;
    private const int BottomCount = 3;

    private readonly IUnitOfWork _uow;

    public GetTimeMachineDailyReportQueryHandler(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<TimeMachineDailyReportDto> Handle(
        GetTimeMachineDailyReportQuery request,
        CancellationToken cancellationToken)
    {
        var date = request.Date.Date;

        var bist = await LoadAsync(TimeMachineCategory.Bist, date, cancellationToken);
        var crypto = await LoadAsync(TimeMachineCategory.Crypto, date, cancellationToken);
        var usStocks = await LoadAsync(TimeMachineCategory.UsStocks, date, cancellationToken);

        var computedAt = bist.Concat(crypto).Concat(usStocks)
            .Select(l => (DateTime?)l.ComputedAt)
            .Max();

        return new TimeMachineDailyReportDto(
            date.ToString("yyyy-MM-dd"),
            MapMarket(bist),
            MapMarket(crypto),
            MapMarket(usStocks),
            computedAt);
    }

    private async Task<IReadOnlyList<TimeMachineLeader>> LoadAsync(
        TimeMachineCategory category,
        DateTime date,
        CancellationToken ct)
        => await _uow.TimeMachineLeaders.GetTopAndBottomForDateAsync(category, date, TopCount, BottomCount, ct);

    private static TimeMachineDailyMarketReportDto MapMarket(IReadOnlyList<TimeMachineLeader> rows)
    {
        var gainers = rows.Where(l => l.Rank > 0).OrderBy(l => l.Rank).Select(Map).ToList();
        var losers = rows.Where(l => l.Rank < 0).OrderByDescending(l => l.Rank).Select(Map).ToList();
        return new TimeMachineDailyMarketReportDto(gainers, losers);
    }

    private static TimeMachineLeaderDto Map(TimeMachineLeader leader)
        => new(
            Math.Abs(leader.Rank),
            leader.Symbol,
            leader.Name,
            leader.StartPrice,
            leader.EndPrice,
            leader.ReturnPct,
            leader.StartPrice > 0m ? Math.Round(leader.EndPrice / leader.StartPrice, 4) : 0m,
            leader.StartDate.ToString("yyyy-MM-dd"),
            leader.EndDate.ToString("yyyy-MM-dd"));
}
