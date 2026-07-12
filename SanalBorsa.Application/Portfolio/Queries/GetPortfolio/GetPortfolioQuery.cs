using MediatR;
using SanalBorsa.Application.DTOs;

namespace SanalBorsa.Application.Portfolio.Queries.GetPortfolio;

public record GetPortfolioQuery(Guid UserId) : IRequest<PortfolioDto>;
