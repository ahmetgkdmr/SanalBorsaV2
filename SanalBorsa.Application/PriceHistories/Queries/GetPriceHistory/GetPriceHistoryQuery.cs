using MediatR;
using SanalBorsa.Application.DTOs;

namespace SanalBorsa.Application.PriceHistories.Queries.GetPriceHistory;

public record GetPriceHistoryQuery(
    string Symbol,
    DateTime? From = null,
    DateTime? To = null
) : IRequest<IReadOnlyList<PriceHistoryDto>>;
