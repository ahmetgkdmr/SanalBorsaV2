using MediatR;
using SanalBorsa.Application.Common.Seeds;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Indices.Queries.GetIndexQuotes;

public class GetIndexQuotesQueryHandler : IRequestHandler<GetIndexQuotesQuery, IReadOnlyList<IndexQuoteDto>>
{
    private readonly IUnitOfWork _uow;

    public GetIndexQuotesQueryHandler(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<IReadOnlyList<IndexQuoteDto>> Handle(
        GetIndexQuotesQuery request,
        CancellationToken cancellationToken)
    {
        var symbols = MarketInstrumentSeed.All.Select(e => e.Symbol).ToList();
        var instruments = await _uow.Stocks.GetBySymbolsAsync(symbols, cancellationToken);
        var snapshots = await _uow.PriceHistories.GetMarketSnapshotsAsync(
            instruments.Select(i => i.Id).ToList(),
            sparklineDays: 28,
            cancellationToken);

        var ordered = MarketInstrumentSeed.All
            .Select(entry =>
            {
                var stock = instruments.FirstOrDefault(i =>
                    i.Symbol.Equals(entry.Symbol, StringComparison.OrdinalIgnoreCase));

                if (stock is null)
                {
                    return new IndexQuoteDto(
                        entry.Symbol,
                        entry.Name,
                        entry.DisplayName,
                        entry.Exchange,
                        0,
                        null,
                        0,
                        true,
                        entry.DisplayDecimals,
                        null,
                        []);
                }

                if (!snapshots.TryGetValue(stock.Id, out var snap) || snap.LastClose is null)
                {
                return new IndexQuoteDto(
                    stock.Symbol,
                    stock.Name,
                    entry.DisplayName,
                    stock.Exchange,
                    0,
                    null,
                    0,
                    true,
                    entry.DisplayDecimals,
                    stock.LatestDataDate,
                    [],
                    stock.EarliestDataDate);
                }

                var value = snap.LastClose.Value;
                var prev = snap.PreviousClose ?? snap.LastOpen ?? value;
                var changePct = prev != 0 ? (value - prev) / prev * 100m : 0m;

                return new IndexQuoteDto(
                    stock.Symbol,
                    stock.Name,
                    entry.DisplayName,
                    stock.Exchange,
                    value,
                    prev,
                    changePct,
                    changePct >= 0,
                    entry.DisplayDecimals,
                    stock.LatestDataDate,
                    snap.Sparkline,
                    stock.EarliestDataDate);
            })
            .ToList();

        return ordered;
    }
}
