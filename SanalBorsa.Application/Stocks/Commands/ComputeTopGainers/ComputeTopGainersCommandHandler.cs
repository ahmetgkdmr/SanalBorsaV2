using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Seeds;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Enums;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Stocks.Commands.ComputeTopGainers;

public class ComputeTopGainersCommandHandler
    : IRequestHandler<ComputeTopGainersCommand, ComputeTopGainersResult>
{
    private static readonly (TopGainerPeriod Period, Func<DateTime, DateTime> Lookback)[] Periods =
    [
        (TopGainerPeriod.Week, d => d.AddDays(-7)),
        (TopGainerPeriod.Month, d => d.AddDays(-30)),
        (TopGainerPeriod.Year, d => d.AddYears(-1)),
        (TopGainerPeriod.FiveYear, d => d.AddYears(-5)),
        (TopGainerPeriod.TenYear, d => d.AddYears(-10)),
    ];

    /// <summary>Stablecoin / fiat çiftleri — dönem şampiyonu olmasınlar.</summary>
    private static readonly HashSet<string> CryptoStableBases = new(StringComparer.OrdinalIgnoreCase)
    {
        "USDC", "FDUSD", "TUSD", "BUSD", "USDP", "DAI", "USD1", "USDE", "USDS", "USDG",
        "PYUSD", "RLUSD", "XUSD", "EURI", "AEUR", "EURC", "EUR", "GBP", "JPY", "TRY", "BRL",
        "USDT",
    };

    private readonly IUnitOfWork _uow;
    private readonly ILogger<ComputeTopGainersCommandHandler> _logger;

    public ComputeTopGainersCommandHandler(
        IUnitOfWork uow,
        ILogger<ComputeTopGainersCommandHandler> logger)
    {
        _uow = uow;
        _logger = logger;
    }

    public async Task<ComputeTopGainersResult> Handle(
        ComputeTopGainersCommand request,
        CancellationToken cancellationToken)
    {
        var market = request.MarketType;

        var stocks = (await _uow.Stocks.GetAllActiveAsync(cancellationToken, market))
            .Where(s => s.MarketType == market)
            .Where(s => !MarketInstrumentSeed.IsMarketInstrument(s.Exchange))
            .Where(s => market != MarketType.Crypto || !IsCryptoStable(s))
            .ToList();

        if (stocks.Count == 0)
        {
            await _uow.TopGainers.ReplaceForMarketAsync(market, [], cancellationToken);
            await _uow.SaveChangesAsync(cancellationToken);
            return new ComputeTopGainersResult(market, DateTime.UtcNow.Date, null, null, null, null, null);
        }

        var stockIds = stocks.Select(s => s.Id).ToList();
        var byId = stocks.ToDictionary(s => s.Id);

        var asOf = await _uow.PriceHistories.GetLatestTradingDateForMarketAsync(market, cancellationToken)
            ?? throw new InvalidOperationException($"{market} için fiyat geçmişi bulunamadı.");

        var endCloses = await _uow.PriceHistories.GetClosesOnOrBeforeAsync(
            stockIds, asOf, cancellationToken);

        var rows = new List<TopGainer>();
        string? week = null, month = null, year = null, fiveYear = null, tenYear = null;
        var now = DateTime.UtcNow;

        foreach (var (period, lookbackFn) in Periods)
        {
            var lookback = lookbackFn(asOf.Date);
            var startCloses = await _uow.PriceHistories.GetClosesOnOrBeforeAsync(
                stockIds, lookback, cancellationToken);

            var ranked = new List<(int StockId, decimal ReturnPct, decimal Start, decimal End, DateTime StartDate)>();

            foreach (var (stockId, end) in endCloses)
            {
                if (!startCloses.TryGetValue(stockId, out var start)) continue;
                if (start.Close <= 0m || end.Close <= 0m) continue;
                if (start.Date >= end.Date) continue;
                if (start.Date > lookback.AddDays(45)) continue;

                var ret = (end.Close - start.Close) / start.Close * 100m;
                ranked.Add((stockId, ret, start.Close, end.Close, start.Date));
            }

            var winner = ranked
                .OrderByDescending(r => r.ReturnPct)
                .ThenBy(r => byId[r.StockId].Symbol)
                .FirstOrDefault();

            if (winner.StockId == 0)
            {
                _logger.LogWarning(
                    "TopGainer {Market}/{Period}: uygun aday yok (lookback={Lookback:yyyy-MM-dd})",
                    market, period, lookback);
                continue;
            }

            var stock = byId[winner.StockId];
            rows.Add(new TopGainer
            {
                MarketType = market,
                Period = period,
                Rank = 1,
                StockId = stock.Id,
                Symbol = stock.Symbol,
                Name = stock.Name,
                ReturnPct = Math.Round(winner.ReturnPct, 4),
                StartPrice = winner.Start,
                EndPrice = winner.End,
                StartDate = winner.StartDate,
                EndDate = endCloses[winner.StockId].Date,
                ComputedAt = now,
            });

            switch (period)
            {
                case TopGainerPeriod.Week: week = stock.Symbol; break;
                case TopGainerPeriod.Month: month = stock.Symbol; break;
                case TopGainerPeriod.Year: year = stock.Symbol; break;
                case TopGainerPeriod.FiveYear: fiveYear = stock.Symbol; break;
                case TopGainerPeriod.TenYear: tenYear = stock.Symbol; break;
            }

            _logger.LogInformation(
                "TopGainer {Market}/{Period}: {Symbol} %+{Return:F2} ({Start:yyyy-MM-dd} → {End:yyyy-MM-dd})",
                market, period, stock.Symbol, winner.ReturnPct, winner.StartDate, asOf);
        }

        await _uow.TopGainers.ReplaceForMarketAsync(market, rows, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        return new ComputeTopGainersResult(market, asOf.Date, week, month, year, fiveYear, tenYear);
    }

    private static bool IsCryptoStable(Stock stock)
    {
        var baseAsset = !string.IsNullOrWhiteSpace(stock.Name)
            ? stock.Name.Trim().ToUpperInvariant()
            : stock.Symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase)
                ? stock.Symbol[..^4]
                : stock.Symbol;
        return CryptoStableBases.Contains(baseAsset);
    }
}
