using MediatR;
using SanalBorsa.Application.DTOs;

namespace SanalBorsa.Application.Stocks.Queries.GetTimeMachineLeaders;

/// <summary>
/// Verilen tarihten bugüne en çok kazandıranlar. Tarih işlem günü değilse
/// önceki en yakın işlem gününe kayılır.
/// </summary>
public record GetTimeMachineLeadersQuery(DateTime Date) : IRequest<TimeMachineLeadersDto>;
