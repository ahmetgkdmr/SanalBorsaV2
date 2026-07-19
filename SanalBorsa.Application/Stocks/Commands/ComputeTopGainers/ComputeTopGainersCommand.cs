using MediatR;

namespace SanalBorsa.Application.Stocks.Commands.ComputeTopGainers;

public record ComputeTopGainersCommand : IRequest<ComputeTopGainersResult>;

public record ComputeTopGainersResult(
    DateTime AsOfDate,
    string? WeekChampion,
    string? MonthChampion,
    string? YearChampion);
