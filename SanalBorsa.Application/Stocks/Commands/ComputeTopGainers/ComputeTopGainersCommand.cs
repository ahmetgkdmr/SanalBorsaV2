using MediatR;
using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Application.Stocks.Commands.ComputeTopGainers;

public record ComputeTopGainersCommand(MarketType MarketType = MarketType.Bist)
    : IRequest<ComputeTopGainersResult>;

public record ComputeTopGainersResult(
    MarketType MarketType,
    DateTime AsOfDate,
    string? WeekChampion,
    string? MonthChampion,
    string? YearChampion,
    string? FiveYearChampion,
    string? TenYearChampion);
