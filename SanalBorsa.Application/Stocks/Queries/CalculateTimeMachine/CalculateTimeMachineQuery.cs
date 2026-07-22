using MediatR;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Application.Stocks.Queries.CalculateTimeMachine;

public record CalculateTimeMachineQuery(
    string Symbol,
    DateTime Date,
    decimal WagePercentage,
    string Mode = "lump",
    decimal? Amount = null,
    MarketType MarketType = MarketType.Bist
) : IRequest<TimeMachineResultDto>;
