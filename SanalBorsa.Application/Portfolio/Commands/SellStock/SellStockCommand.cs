using MediatR;
using SanalBorsa.Application.DTOs;

namespace SanalBorsa.Application.Portfolio.Commands.SellStock;

public record SellStockCommand(Guid UserId, string Symbol, long Lots) : IRequest<PortfolioDto>;
