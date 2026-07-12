using MediatR;
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
        var portfolio = await _uow.Portfolios.GetByUserIdAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("Portfolio", request.UserId);

        var holding = portfolio.Holdings.FirstOrDefault(h => h.Symbol == request.Symbol)
            ?? throw new InvalidOperationException($"Portföyde {request.Symbol} bulunamadı.");

        if (holding.Lots < request.Lots)
            throw new InvalidOperationException("Yeterli lot yok.");

        var stock = await _uow.Stocks.GetBySymbolAsync(request.Symbol.ToUpperInvariant(), cancellationToken)
            ?? throw new NotFoundException("Stock", request.Symbol);

        var snapshot = await _uow.PriceHistories.GetMarketSnapshotsAsync(
            [stock.Id], sparklineDays: 1, ct: cancellationToken);

        if (!snapshot.TryGetValue(stock.Id, out var snap) || snap.LastClose is null)
            throw new InvalidOperationException($"{request.Symbol} için güncel fiyat bulunamadı.");

        var price = snap.LastClose.Value;
        var total = price * request.Lots;

        holding.Lots -= request.Lots;
        if (holding.Lots == 0)
            portfolio.Holdings.Remove(holding);

        portfolio.Cash += total;

        portfolio.Transactions.Add(new PortfolioTransaction
        {
            Id          = Guid.NewGuid(),
            PortfolioId = portfolio.Id,
            Symbol      = request.Symbol,
            Side        = TxSide.Sell,
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
