using System.Text.Json;
using MediatR;
using SanalBorsa.Application.Common.Exceptions;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Portfolio.Commands.BuyCrypto;

public record BuyCryptoCommand(
    Guid UserId,
    string Symbol,
    decimal? TryAmount,
    decimal? QuoteUsd,
    decimal? Quantity) : IRequest<CryptoTradeResultDto>;

public record CryptoTradeResultDto(
    PortfolioDto Portfolio,
    CryptoFillPreviewDto Fill);

public class BuyCryptoCommandHandler : IRequestHandler<BuyCryptoCommand, CryptoTradeResultDto>
{
    private readonly IUnitOfWork _uow;
    private readonly ICryptoMarketService _crypto;
    private readonly IPortfolioFxRateProvider _fx;

    public BuyCryptoCommandHandler(IUnitOfWork uow, ICryptoMarketService crypto, IPortfolioFxRateProvider fx)
    {
        _uow = uow;
        _crypto = crypto;
        _fx = fx;
    }

    public async Task<CryptoTradeResultDto> Handle(BuyCryptoCommand request, CancellationToken cancellationToken)
    {
        var portfolio = await _uow.Portfolios.GetByUserIdAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("Portfolio", request.UserId);

        decimal? quoteUsd = request.QuoteUsd;
        var rate = await _fx.GetUsdTryRateAsync(cancellationToken);
        if (request.TryAmount is { } tryAmount)
            quoteUsd = tryAmount / rate;

        var fill = await _crypto.PreviewBuyAsync(
            request.Symbol, quoteUsd, request.Quantity, cancellationToken);

        if (!fill.FullyFilled || fill.FilledQuantity <= 0)
            throw new InvalidOperationException(
                "Derinlik yetersiz — emir tamamen doldurulamadı. Daha küçük tutar deneyin.");

        var tryEquivalent = fill.Total * rate;
        if (tryEquivalent > portfolio.Cash)
            throw new InvalidOperationException("Yetersiz bakiye.");

        portfolio.Cash -= tryEquivalent;

        var symbol = fill.Symbol;
        var existing = portfolio.Holdings.FirstOrDefault(h =>
            h.Symbol == symbol && h.MarketType == MarketType.Crypto);

        if (existing is not null)
        {
            var newQty = existing.Quantity + fill.FilledQuantity;
            existing.AvgCost =
                (existing.AvgCost * existing.Quantity + fill.Total) / newQty;
            existing.Quantity = newQty;
        }
        else
        {
            portfolio.Holdings.Add(new PortfolioHolding
            {
                PortfolioId = portfolio.Id,
                Symbol      = symbol,
                MarketType  = MarketType.Crypto,
                Quantity    = fill.FilledQuantity,
                AvgCost     = fill.AvgPrice,
            });
        }

        portfolio.Transactions.Add(new PortfolioTransaction
        {
            PortfolioId       = portfolio.Id,
            Symbol            = symbol,
            MarketType        = MarketType.Crypto,
            Side              = TxSide.Buy,
            Quantity          = fill.FilledQuantity,
            Price             = fill.AvgPrice,
            Total             = fill.Total,
            FillBreakdownJson = JsonSerializer.Serialize(fill.Levels),
            ExchangeRateAtTrade = rate,
            ExecutedAt        = DateTime.UtcNow,
        });

        portfolio.UpdatedAt = DateTime.UtcNow;
        await _uow.SaveChangesAsync(cancellationToken);

        return new CryptoTradeResultDto(PortfolioDto.FromEntity(portfolio), fill);
    }
}
