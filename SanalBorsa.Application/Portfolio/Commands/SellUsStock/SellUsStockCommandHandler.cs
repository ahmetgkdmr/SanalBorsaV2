using MediatR;
using SanalBorsa.Application.Common;
using SanalBorsa.Application.Common.Exceptions;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Portfolio.Commands.SellUsStock;

public class SellUsStockCommandHandler : IRequestHandler<SellUsStockCommand, PortfolioDto>
{
    private readonly IUnitOfWork _uow;
    private readonly IPortfolioFxRateProvider _fx;

    public SellUsStockCommandHandler(IUnitOfWork uow, IPortfolioFxRateProvider fx)
    {
        _uow = uow;
        _fx = fx;
    }

    public async Task<PortfolioDto> Handle(SellUsStockCommand request, CancellationToken cancellationToken)
    {
        NyseTradingHours.EnsureOpen();

        if (request.Quantity <= 0)
            throw new InvalidOperationException("Miktar 0'dan büyük olmalıdır.");

        var portfolio = await _uow.Portfolios.GetByUserIdAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("Portfolio", request.UserId);

        var symbol = request.Symbol.ToUpperInvariant();
        var holding = portfolio.Holdings.FirstOrDefault(h =>
                h.Symbol == symbol && h.MarketType == MarketType.UsStocks)
            ?? throw new InvalidOperationException($"Portföyde {request.Symbol} bulunamadı.");

        if (holding.Quantity < request.Quantity)
            throw new InvalidOperationException("Yeterli miktar yok.");

        var stock = await _uow.Stocks.GetBySymbolAsync(symbol, cancellationToken, MarketType.UsStocks)
            ?? throw new NotFoundException("Stock", request.Symbol);

        var snapshot = await _uow.PriceHistories.GetMarketSnapshotsAsync(
            [stock.Id], sparklineDays: 1, ct: cancellationToken);

        if (!snapshot.TryGetValue(stock.Id, out var snap) || snap.LastClose is null)
            throw new InvalidOperationException($"{request.Symbol} için güncel fiyat bulunamadı.");

        var priceUsd = snap.LastClose.Value;
        var usdAmount = request.Quantity * priceUsd;
        var rate = await _fx.GetUsdTryRateAsync(cancellationToken);
        var tryAmount = usdAmount * rate;

        holding.Quantity -= request.Quantity;
        if (holding.Quantity <= 0.00000001m)
            portfolio.Holdings.Remove(holding);

        portfolio.Cash += tryAmount;

        portfolio.Transactions.Add(new PortfolioTransaction
        {
            PortfolioId          = portfolio.Id,
            Symbol               = symbol,
            MarketType           = MarketType.UsStocks,
            Side                 = TxSide.Sell,
            Quantity             = request.Quantity,
            Price                = priceUsd,
            Total                = usdAmount,
            ExchangeRateAtTrade  = rate,
            ExecutedAt           = DateTime.UtcNow,
        });

        portfolio.UpdatedAt = DateTime.UtcNow;
        await _uow.SaveChangesAsync(cancellationToken);

        return PortfolioDto.FromEntity(portfolio);
    }
}
