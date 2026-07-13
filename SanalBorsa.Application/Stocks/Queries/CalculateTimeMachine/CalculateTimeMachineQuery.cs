using MediatR;
using SanalBorsa.Application.DTOs;

namespace SanalBorsa.Application.Stocks.Queries.CalculateTimeMachine;

public record CalculateTimeMachineQuery(
    string Symbol,
    DateTime Date,
    decimal WagePercentage,
    string Mode = "lump",
    decimal? Amount = null          // Belirlenmişse asgari ücret hesabını geçersiz kılar
) : IRequest<TimeMachineResultDto>;
