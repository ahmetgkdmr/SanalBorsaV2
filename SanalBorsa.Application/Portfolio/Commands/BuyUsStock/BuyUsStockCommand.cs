using MediatR;
using SanalBorsa.Application.DTOs;

namespace SanalBorsa.Application.Portfolio.Commands.BuyUsStock;

/// <summary>TL tutarı girilir, anlık USD/TRY kuruyla çevrilip kesirli hisse adedi hesaplanır.</summary>
public record BuyUsStockCommand(Guid UserId, string Symbol, decimal TryAmount) : IRequest<PortfolioDto>;
