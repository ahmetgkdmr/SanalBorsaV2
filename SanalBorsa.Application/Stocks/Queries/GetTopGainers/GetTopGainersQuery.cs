using MediatR;
using SanalBorsa.Application.DTOs;

namespace SanalBorsa.Application.Stocks.Queries.GetTopGainers;

public record GetTopGainersQuery : IRequest<TopGainersResponseDto>;
