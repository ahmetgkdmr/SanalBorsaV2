using MediatR;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Crypto.Queries.GetCryptoStockMeta;

public record GetCryptoStockMetaQuery(string Symbol) : IRequest<StockDto?>;

public sealed class GetCryptoStockMetaQueryHandler : IRequestHandler<GetCryptoStockMetaQuery, StockDto?>
{
    private readonly IUnitOfWork _uow;

    public GetCryptoStockMetaQueryHandler(IUnitOfWork uow) => _uow = uow;

    public async Task<StockDto?> Handle(GetCryptoStockMetaQuery request, CancellationToken cancellationToken)
    {
        var symbol = request.Symbol.Trim().ToUpperInvariant();
        if (!symbol.EndsWith("USDT", StringComparison.Ordinal))
            symbol += "USDT";

        var stock = await _uow.Stocks.GetBySymbolAsync(symbol, cancellationToken, MarketType.Crypto);
        if (stock is null) return null;

        return new StockDto(
            stock.Id,
            stock.Symbol,
            stock.Name,
            stock.Sector,
            stock.Industry,
            stock.Currency,
            stock.Exchange,
            stock.IsActive,
            stock.EarliestDataDate,
            stock.LatestDataDate,
            stock.NeedsHistoryRefresh,
            "crypto");
    }
}
