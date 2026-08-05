using MediatR;
using SanalBorsa.Application.Common;
using SanalBorsa.Application.Common.Exceptions;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Portfolio.Commands.BuyUsStock;

public class BuyUsStockCommandHandler : IRequestHandler<BuyUsStockCommand, PortfolioDto>
{
    private readonly IUnitOfWork _uow;
    private readonly IPortfolioFxRateProvider _fx;

    public BuyUsStockCommandHandler(IUnitOfWork uow, IPortfolioFxRateProvider fx)
    {
        _uow = uow;
        _fx = fx;
    }

    public async Task<PortfolioDto> Handle(BuyUsStockCommand request, CancellationToken cancellationToken)
    {
        NyseTradingHours.EnsureOpen();

        if (request.TryAmount <= 0)
            throw new InvalidOperationException("Tutar 0'dan büyük olmalıdır.");

        var portfolio = await _uow.Portfolios.GetByUserIdAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("Portfolio", request.UserId);

        if (request.TryAmount > portfolio.Cash)
            throw new InvalidOperationException("Yetersiz bakiye.");

        var symbol = request.Symbol.ToUpperInvariant();
        var stock = await _uow.Stocks.GetBySymbolAsync(symbol, cancellationToken, MarketType.UsStocks)
            ?? throw new NotFoundException("Stock", request.Symbol);

        var snapshot = await _uow.PriceHistories.GetMarketSnapshotsAsync(
            [stock.Id], sparklineDays: 1, ct: cancellationToken);

        if (!snapshot.TryGetValue(stock.Id, out var snap) || snap.LastClose is null)
            throw new InvalidOperationException($"{request.Symbol} için güncel fiyat bulunamadı.");

        var priceUsd = snap.LastClose.Value;
        var rate = await _fx.GetUsdTryRateAsync(cancellationToken);
        var usdAmount = request.TryAmount / rate;
        var qty = usdAmount / priceUsd;

        portfolio.Cash -= request.TryAmount;

        var existing = portfolio.Holdings.FirstOrDefault(h =>
            h.Symbol == symbol && h.MarketType == MarketType.UsStocks);
        if (existing is not null)
        {
            var newQty = existing.Quantity + qty;
            existing.AvgCost = (existing.AvgCost * existing.Quantity + usdAmount) / newQty;
            existing.Quantity = newQty;
        }
        else
        {
            portfolio.Holdings.Add(new PortfolioHolding
            {
                PortfolioId = portfolio.Id,
                Symbol      = symbol,
                MarketType  = MarketType.UsStocks,
                Quantity    = qty,
                AvgCost     = priceUsd,
            });
        }

        portfolio.Transactions.Add(new PortfolioTransaction
        {
            PortfolioId          = portfolio.Id,
            Symbol               = symbol,
            MarketType           = MarketType.UsStocks,
            Side                 = TxSide.Buy,
            Quantity             = qty,
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
