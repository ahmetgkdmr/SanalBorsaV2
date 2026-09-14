using System.Text.Json;
using MediatR;
using SanalBorsa.Application.Common;
using SanalBorsa.Application.Common.Exceptions;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Portfolio.Commands.SellCrypto;

public record SellCryptoCommand(
    Guid UserId,
    string Symbol,
    decimal Quantity) : IRequest<BuyCrypto.CryptoTradeResultDto>;

public class SellCryptoCommandHandler
    : IRequestHandler<SellCryptoCommand, BuyCrypto.CryptoTradeResultDto>
{
    private readonly IUnitOfWork _uow;
    private readonly ICryptoMarketService _crypto;
    private readonly IPortfolioFxRateProvider _fx;

    public SellCryptoCommandHandler(IUnitOfWork uow, ICryptoMarketService crypto, IPortfolioFxRateProvider fx)
    {
        _uow = uow;
        _crypto = crypto;
        _fx = fx;
    }

    public Task<BuyCrypto.CryptoTradeResultDto> Handle(
        SellCryptoCommand request, CancellationToken cancellationToken)
        => ConcurrencySafe.RunAsync(_uow, () => ExecuteAsync(request, cancellationToken));

    private async Task<BuyCrypto.CryptoTradeResultDto> ExecuteAsync(
        SellCryptoCommand request, CancellationToken cancellationToken)
    {
        if (request.Quantity <= 0)
            throw new BusinessRuleException("Miktar 0'dan büyük olmalıdır.");

        var portfolio = await _uow.Portfolios.GetByUserIdAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("Portfolio", request.UserId);

        var fill = await _crypto.PreviewSellAsync(request.Symbol, request.Quantity, cancellationToken);
        var symbol = fill.Symbol;

        var holding = portfolio.Holdings.FirstOrDefault(h =>
                h.Symbol == symbol && h.MarketType == MarketType.Crypto)
            ?? throw new BusinessRuleException($"Portföyde {symbol} bulunamadı.");

        if (holding.Quantity < request.Quantity)
            throw new BusinessRuleException("Yeterli miktar yok.");

        if (!fill.FullyFilled || fill.FilledQuantity <= 0)
            throw new BusinessRuleException(
                "Derinlik yetersiz — emir tamamen doldurulamadı. Daha küçük miktar deneyin.");

        holding.Quantity -= fill.FilledQuantity;
        if (holding.Quantity <= 0.00000001m)
            portfolio.Holdings.Remove(holding);

        var rate = await _fx.GetUsdTryRateAsync(cancellationToken);
        var tryEquivalent = fill.Total * rate;
        portfolio.Cash += tryEquivalent;

        portfolio.Transactions.Add(new PortfolioTransaction
        {
            PortfolioId       = portfolio.Id,
            Symbol            = symbol,
            MarketType        = MarketType.Crypto,
            Side              = TxSide.Sell,
            Quantity          = fill.FilledQuantity,
            Price             = fill.AvgPrice,
            Total             = fill.Total,
            FillBreakdownJson = JsonSerializer.Serialize(fill.Levels),
            ExchangeRateAtTrade = rate,
            ExecutedAt        = DateTime.UtcNow,
        });

        portfolio.UpdatedAt = DateTime.UtcNow;
        await _uow.SaveChangesAsync(cancellationToken);

        return new BuyCrypto.CryptoTradeResultDto(PortfolioDto.FromEntity(portfolio), fill);
    }
}
