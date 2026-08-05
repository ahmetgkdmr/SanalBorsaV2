using MediatR;
using SanalBorsa.Application.DTOs;

namespace SanalBorsa.Application.Portfolio.Commands.SellUsStock;

/// <summary>Kesirli miktar satılır, gelen USD tutarı anlık kurla TL'ye çevrilip bakiyeye eklenir.</summary>
public record SellUsStockCommand(Guid UserId, string Symbol, decimal Quantity) : IRequest<PortfolioDto>;
