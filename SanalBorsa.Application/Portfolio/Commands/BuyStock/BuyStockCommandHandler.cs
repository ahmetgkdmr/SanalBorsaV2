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
        if (request.Lots <= 0)
            throw new InvalidOperationException("Lot sayısı 0'dan büyük olmalıdır.");

        var portfolio = await _uow.Portfolios.GetByUserIdAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("Portfolio", request.UserId);

        var symbol = request.Symbol.ToUpperInvariant();
        var stock = await _uow.Stocks.GetBySymbolAsync(symbol, cancellationToken)
            ?? throw new NotFoundException("Stock", request.Symbol);

        var snapshot = await _uow.PriceHistories.GetMarketSnapshotsAsync(
            [stock.Id], sparklineDays: 1, ct: cancellationToken);

        if (!snapshot.TryGetValue(stock.Id, out var snap) || snap.LastClose is null)
            throw new InvalidOperationException($"{request.Symbol} için güncel fiyat bulunamadı.");

        var price = snap.LastClose.Value;
        var qty = (decimal)request.Lots;
        var total = price * qty;

        if (total > portfolio.Cash)
            throw new InvalidOperationException("Yetersiz bakiye.");

        portfolio.Cash -= total;

        var existing = portfolio.Holdings.FirstOrDefault(h =>
            h.Symbol == symbol && h.MarketType == MarketType.Bist);
        if (existing is not null)
        {
            var newQty = existing.Quantity + qty;
            existing.AvgCost = (existing.AvgCost * existing.Quantity + total) / newQty;
            existing.Quantity = newQty;
        }
        else
        {
            portfolio.Holdings.Add(new PortfolioHolding
            {
                PortfolioId = portfolio.Id,
                Symbol      = symbol,
                MarketType  = MarketType.Bist,
                Quantity    = qty,
                AvgCost     = price,
            });
        }

        portfolio.Transactions.Add(new PortfolioTransaction
        {
            PortfolioId = portfolio.Id,
            Symbol      = symbol,
            MarketType  = MarketType.Bist,
            Side        = TxSide.Buy,
            Quantity    = qty,
            Price       = price,
            Total       = total,
            ExecutedAt  = DateTime.UtcNow,
        });

        portfolio.UpdatedAt = DateTime.UtcNow;
        // Portfolio zaten tracked; Update() yeni Holding/Transaction'ları Modified sayıp
        // concurrency hatasına (0 row affected) yol açar.
        await _uow.SaveChangesAsync(cancellationToken);

        return PortfolioDto.FromEntity(portfolio);
    }
}
