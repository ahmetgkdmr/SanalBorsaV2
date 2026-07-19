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
    private static readonly (TopGainerPeriod Period, int LookbackDays)[] Periods =
    [
        (TopGainerPeriod.Week, 7),
        (TopGainerPeriod.Month, 30),
        (TopGainerPeriod.Year, 365),
    ];

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
        var asOf = await _uow.PriceHistories.GetLatestTradingDateAsync(cancellationToken)
            ?? throw new InvalidOperationException("Fiyat geçmişi bulunamadı.");

        var stocks = (await _uow.Stocks.GetAllActiveAsync(cancellationToken))
            .Where(s => !MarketInstrumentSeed.IsMarketInstrument(s.Exchange))
            .ToList();

        var stockIds = stocks.Select(s => s.Id).ToList();
        var byId = stocks.ToDictionary(s => s.Id);

        var endCloses = await _uow.PriceHistories.GetClosesOnOrBeforeAsync(
            stockIds, asOf, cancellationToken);

        var rows = new List<TopGainer>();
        string? week = null, month = null, year = null;
        var now = DateTime.UtcNow;

        foreach (var (period, days) in Periods)
        {
            var lookback = asOf.Date.AddDays(-days);
            var startCloses = await _uow.PriceHistories.GetClosesOnOrBeforeAsync(
                stockIds, lookback, cancellationToken);

            var ranked = new List<(int StockId, decimal ReturnPct, decimal Start, decimal End, DateTime StartDate)>();

            foreach (var (stockId, end) in endCloses)
            {
                if (!startCloses.TryGetValue(stockId, out var start)) continue;
                if (start.Close <= 0m || end.Close <= 0m) continue;
                // Dönem boyunca gerçekten geçmiş olsun (aynı günü kullanma)
                if (start.Date >= end.Date) continue;

                var ret = (end.Close - start.Close) / start.Close * 100m;
                ranked.Add((stockId, ret, start.Close, end.Close, start.Date));
            }

            var winner = ranked
                .OrderByDescending(r => r.ReturnPct)
                .ThenBy(r => byId[r.StockId].Symbol)
                .FirstOrDefault();

            if (winner.StockId == 0) continue;

            var stock = byId[winner.StockId];
            rows.Add(new TopGainer
            {
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
            }

            _logger.LogInformation(
                "TopGainer {Period}: {Symbol} %+{Return:F2} ({Start:yyyy-MM-dd} → {End:yyyy-MM-dd})",
                period, stock.Symbol, winner.ReturnPct, winner.StartDate, asOf);
        }

        await _uow.TopGainers.ReplaceAllAsync(rows, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        return new ComputeTopGainersResult(asOf.Date, week, month, year);
    }
}
