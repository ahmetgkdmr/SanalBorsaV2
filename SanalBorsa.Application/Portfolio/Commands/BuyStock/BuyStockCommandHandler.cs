using MediatR;
using SanalBorsa.Application.Common.Exceptions;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Portfolio.Commands.BuyStock;

public class BuyStockCommandHandler : IRequestHandler<BuyStockCommand, PortfolioDto>
{
    private readonly IUnitOfWork _uow;

    public BuyStockCommandHandler(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<PortfolioDto> Handle(BuyStockCommand request, CancellationToken cancellationToken)
    {
        var portfolio = await _uow.Portfolios.GetByUserIdAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("Portfolio", request.UserId);

        var stock = await _uow.Stocks.GetBySymbolAsync(request.Symbol.ToUpperInvariant(), cancellationToken)
            ?? throw new NotFoundException("Stock", request.Symbol);

        var snapshot = await _uow.PriceHistories.GetMarketSnapshotsAsync(
            [stock.Id], sparklineDays: 1, ct: cancellationToken);

        if (!snapshot.TryGetValue(stock.Id, out var snap) || snap.LastClose is null)
            throw new InvalidOperationException($"{request.Symbol} için güncel fiyat bulunamadı.");

        var price = snap.LastClose.Value;
        var total = price * request.Lots;

        if (total > portfolio.Cash)
            throw new InvalidOperationException("Yetersiz bakiye.");

        portfolio.Cash -= total;

        var existing = portfolio.Holdings.FirstOrDefault(h => h.Symbol == request.Symbol);
        if (existing is not null)
        {
            var newLots = existing.Lots + request.Lots;
            existing.AvgCost = (existing.AvgCost * existing.Lots + total) / newLots;
            existing.Lots    = newLots;
        }
        else
        {
            portfolio.Holdings.Add(new PortfolioHolding
            {
                Id          = Guid.NewGuid(),
                PortfolioId = portfolio.Id,
                Symbol      = request.Symbol,
                Lots        = request.Lots,
                AvgCost     = price,
            });
        }

        portfolio.Transactions.Add(new PortfolioTransaction
        {
            Id          = Guid.NewGuid(),
            PortfolioId = portfolio.Id,
            Symbol      = request.Symbol,
            Side        = TxSide.Buy,
            Lots        = request.Lots,
            Price       = price,
            Total       = total,
            ExecutedAt  = DateTime.UtcNow,
        });

        portfolio.UpdatedAt = DateTime.UtcNow;
        _uow.Portfolios.Update(portfolio);
        await _uow.SaveChangesAsync(cancellationToken);

        return PortfolioDto.FromEntity(portfolio);
    }
}
