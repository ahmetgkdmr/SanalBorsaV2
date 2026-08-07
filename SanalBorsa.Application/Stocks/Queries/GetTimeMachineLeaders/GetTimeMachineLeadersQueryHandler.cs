using MediatR;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Enums;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Stocks.Queries.GetTimeMachineLeaders;

public class GetTimeMachineLeadersQueryHandler
    : IRequestHandler<GetTimeMachineLeadersQuery, TimeMachineLeadersDto>
{
    private readonly IUnitOfWork _uow;

    public GetTimeMachineLeadersQueryHandler(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<TimeMachineLeadersDto> Handle(
        GetTimeMachineLeadersQuery request,
        CancellationToken cancellationToken)
    {
        var date = request.Date.Date;

        var bist = await LoadAsync(TimeMachineCategory.Bist, date, cancellationToken);
        var crypto = await LoadAsync(TimeMachineCategory.Crypto, date, cancellationToken);
        var usStocks = await LoadAsync(TimeMachineCategory.UsStocks, date, cancellationToken);
        var parity = await LoadAsync(TimeMachineCategory.Parity, date, cancellationToken);

        var computedAt = bist.Concat(crypto).Concat(usStocks).Concat(parity)
            .Select(l => (DateTime?)l.ComputedAt)
            .Max();

        return new TimeMachineLeadersDto(
            date.ToString("yyyy-MM-dd"),
            bist.Select(Map).ToList(),
            crypto.Select(Map).ToList(),
            usStocks.Select(Map).ToList(),
            parity.Select(Map).ToList(),
            computedAt);
    }

    private async Task<IReadOnlyList<TimeMachineLeader>> LoadAsync(
        TimeMachineCategory category,
        DateTime date,
        CancellationToken ct)
        => await _uow.TimeMachineLeaders.GetForDateAsync(category, date, ct);

    private static TimeMachineLeaderDto Map(TimeMachineLeader leader)
        => new(
            leader.Rank,
            leader.Symbol,
            leader.Name,
            leader.StartPrice,
            leader.EndPrice,
            leader.ReturnPct,
            leader.StartPrice > 0m ? Math.Round(leader.EndPrice / leader.StartPrice, 4) : 0m,
            leader.StartDate.ToString("yyyy-MM-dd"),
            leader.EndDate.ToString("yyyy-MM-dd"));
}
