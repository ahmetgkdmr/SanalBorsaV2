using MediatR;
using SanalBorsa.Application.Common;
using SanalBorsa.Application.Common.Exceptions;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Portfolio.Commands.SellStock;

public class SellStockCommandHandler : IRequestHandler<SellStockCommand, PortfolioDto>
{
    private readonly IUnitOfWork _uow;

    public SellStockCommandHandler(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<PortfolioDto> Handle(SellStockCommand request, CancellationToken cancellationToken)
    {
        BistTradingHours.EnsureOpen();

        if (request.Lots <= 0)
            throw new InvalidOperationException("Lot sayısı 0'dan büyük olmalıdır.");

        var portfolio = await _uow.Portfolios.GetByUserIdAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("Portfolio", request.UserId);

        var symbol = request.Symbol.ToUpperInvariant();
        var qty = (decimal)request.Lots;

        var holding = portfolio.Holdings.FirstOrDefault(h =>
                h.Symbol == symbol && h.MarketType == MarketType.Bist)
            ?? throw new InvalidOperationException($"Portföyde {request.Symbol} bulunamadı.");

        if (holding.Quantity < qty)
            throw new InvalidOperationException("Yeterli lot yok.");

        var stock = await _uow.Stocks.GetBySymbolAsync(symbol, cancellationToken)
            ?? throw new NotFoundException("Stock", request.Symbol);

        var snapshot = await _uow.PriceHistories.GetMarketSnapshotsAsync(
            [stock.Id], sparklineDays: 1, ct: cancellationToken);

        if (!snapshot.TryGetValue(stock.Id, out var snap) || snap.LastClose is null)
            throw new InvalidOperationException($"{request.Symbol} için güncel fiyat bulunamadı.");

        var price = snap.LastClose.Value;
        var total = price * qty;

        holding.Quantity -= qty;
        if (holding.Quantity == 0)
            portfolio.Holdings.Remove(holding);

        portfolio.Cash += total;

        portfolio.Transactions.Add(new PortfolioTransaction
        {
            PortfolioId = portfolio.Id,
            Symbol      = symbol,
            MarketType  = MarketType.Bist,
            Side        = TxSide.Sell,
            Quantity    = qty,
            Price       = price,
            Total       = total,
            ExecutedAt  = DateTime.UtcNow,
        });

        portfolio.UpdatedAt = DateTime.UtcNow;
        await _uow.SaveChangesAsync(cancellationToken);

        return PortfolioDto.FromEntity(portfolio);
    }
}
