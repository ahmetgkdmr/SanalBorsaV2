using MediatR;
using SanalBorsa.Application.DTOs;

namespace SanalBorsa.Application.Portfolio.Commands.BuyStock;

public record BuyStockCommand(Guid UserId, string Symbol, long Lots) : IRequest<PortfolioDto>;
