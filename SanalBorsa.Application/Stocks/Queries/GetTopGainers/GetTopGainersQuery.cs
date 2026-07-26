using MediatR;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Application.Stocks.Queries.GetTopGainers;

public record GetTopGainersQuery(MarketType MarketType = MarketType.Bist)
    : IRequest<TopGainersResponseDto>;
