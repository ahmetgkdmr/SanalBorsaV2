using System.Text.Json;
using MediatR;
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

    public SellCryptoCommandHandler(IUnitOfWork uow, ICryptoMarketService crypto)
    {
        _uow = uow;
        _crypto = crypto;
    }

    public async Task<BuyCrypto.CryptoTradeResultDto> Handle(
        SellCryptoCommand request, CancellationToken cancellationToken)
    {
        if (request.Quantity <= 0)
            throw new InvalidOperationException("Miktar 0'dan büyük olmalıdır.");

        var portfolio = await _uow.Portfolios.GetByUserIdAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("Portfolio", request.UserId);

        var fill = await _crypto.PreviewSellAsync(request.Symbol, request.Quantity, cancellationToken);
        var symbol = fill.Symbol;

        var holding = portfolio.Holdings.FirstOrDefault(h =>
                h.Symbol == symbol && h.MarketType == MarketType.Crypto)
            ?? throw new InvalidOperationException($"Portföyde {symbol} bulunamadı.");

        if (holding.Quantity < request.Quantity)
            throw new InvalidOperationException("Yeterli miktar yok.");

        if (!fill.FullyFilled || fill.FilledQuantity <= 0)
            throw new InvalidOperationException(
                "Derinlik yetersiz — emir tamamen doldurulamadı. Daha küçük miktar deneyin.");

        holding.Quantity -= fill.FilledQuantity;
        if (holding.Quantity <= 0.00000001m)
            portfolio.Holdings.Remove(holding);

        portfolio.CashUsd += fill.Total;

        portfolio.Transactions.Add(new PortfolioTransaction
        {
            Id                = Guid.NewGuid(),
            PortfolioId       = portfolio.Id,
            Symbol            = symbol,
            MarketType        = MarketType.Crypto,
            Side              = TxSide.Sell,
            Quantity          = fill.FilledQuantity,
            Price             = fill.AvgPrice,
            Total             = fill.Total,
            FillBreakdownJson = JsonSerializer.Serialize(fill.Levels),
            ExecutedAt        = DateTime.UtcNow,
        });

        portfolio.UpdatedAt = DateTime.UtcNow;
        _uow.Portfolios.Update(portfolio);
        await _uow.SaveChangesAsync(cancellationToken);

        return new BuyCrypto.CryptoTradeResultDto(PortfolioDto.FromEntity(portfolio), fill);
    }
}
