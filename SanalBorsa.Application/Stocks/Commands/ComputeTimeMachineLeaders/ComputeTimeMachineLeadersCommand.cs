using MediatR;
using SanalBorsa.Domain.Enums;

namespace SanalBorsa.Application.Stocks.Commands.ComputeTimeMachineLeaders;

/// <summary>
/// Her işlem günü için "o gün alsaydın bugüne kadar" tablosunu yeniden üretir.
/// <paramref name="Category"/> boşsa üç kategori de hesaplanır.
/// </summary>
public record ComputeTimeMachineLeadersCommand(TimeMachineCategory? Category = null)
    : IRequest<ComputeTimeMachineLeadersResult>;

public record TimeMachineCategoryResult(
    TimeMachineCategory Category,
    int Days,
    int Rows,
    DateTime? EarliestStartDate,
    DateTime? EndDate,
    long ElapsedMs,
    string? Error);

public record ComputeTimeMachineLeadersResult(
    IReadOnlyList<TimeMachineCategoryResult> Categories,
    long ElapsedMs);
