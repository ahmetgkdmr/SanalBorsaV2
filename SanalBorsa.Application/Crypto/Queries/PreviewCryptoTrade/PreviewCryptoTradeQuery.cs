using MediatR;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Application.Common.Exceptions;

namespace SanalBorsa.Application.Crypto.Queries.PreviewCryptoTrade;

public record PreviewCryptoTradeQuery(
    string Symbol,
    string Side,
    decimal? QuoteUsd,
    decimal? Quantity) : IRequest<CryptoFillPreviewDto>;

public class PreviewCryptoTradeQueryHandler
    : IRequestHandler<PreviewCryptoTradeQuery, CryptoFillPreviewDto>
{
    private readonly ICryptoMarketService _crypto;

    public PreviewCryptoTradeQueryHandler(ICryptoMarketService crypto) => _crypto = crypto;

    public Task<CryptoFillPreviewDto> Handle(
        PreviewCryptoTradeQuery request, CancellationToken cancellationToken)
    {
        var side = request.Side.Trim().ToLowerInvariant();
        if (side == "buy")
            return _crypto.PreviewBuyAsync(request.Symbol, request.QuoteUsd, request.Quantity, cancellationToken);
        if (side == "sell")
        {
            if (request.Quantity is null or <= 0)
                throw new BusinessRuleException("Satış için quantity gerekli.");
            return _crypto.PreviewSellAsync(request.Symbol, request.Quantity.Value, cancellationToken);
        }

        throw new BusinessRuleException("Side 'buy' veya 'sell' olmalıdır.");
    }
}
