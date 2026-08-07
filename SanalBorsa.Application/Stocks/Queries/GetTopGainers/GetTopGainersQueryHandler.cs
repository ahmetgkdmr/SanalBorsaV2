using MediatR;
using Microsoft.Extensions.Caching.Memory;
using SanalBorsa.Application.Common;
using SanalBorsa.Application.Common.Seeds;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Stocks.Queries.GetTopGainers;

public class GetTopGainersQueryHandler : IRequestHandler<GetTopGainersQuery, TopGainersResponseDto>
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    private readonly IUnitOfWork _uow;
    private readonly IMemoryCache _cache;
    private readonly MarketDataCacheVersion _cacheVersion;

    public GetTopGainersQueryHandler(
        IUnitOfWork uow,
        IMemoryCache cache,
        MarketDataCacheVersion cacheVersion)
    {
        _uow = uow;
        _cache = cache;
        _cacheVersion = cacheVersion;
    }

    public async Task<TopGainersResponseDto> Handle(
        GetTopGainersQuery request,
        CancellationToken cancellationToken)
    {
        var version = request.MarketType switch
        {
            MarketType.Crypto => _cacheVersion.Crypto,
            MarketType.UsStocks => _cacheVersion.Us,
            _ => _cacheVersion.Bist,
        };
        var cacheKey = $"top-gainers:{request.MarketType}:v{version}";

        if (_cache.TryGetValue(cacheKey, out TopGainersResponseDto? cached) && cached is not null)
            return cached;

        var result = await ComputeAsync(request, cancellationToken);
        _cache.Set(cacheKey, result, CacheTtl);
        return result;
    }

    private async Task<TopGainersResponseDto> ComputeAsync(
        GetTopGainersQuery request,
        CancellationToken cancellationToken)
    {
        var rows = await _uow.TopGainers.GetByMarketAsync(request.MarketType, cancellationToken);
        if (rows.Count == 0)
            return new TopGainersResponseDto(null, null, []);

        var stockIds = rows.Select(r => r.StockId).Distinct().ToList();
        var snapshots = await _uow.PriceHistories.GetMarketSnapshotsAsync(
            stockIds, sparklineDays: 28, cancellationToken);

        var items = rows
            .OrderBy(r => TopGainerPeriodInfo.SortOrder(r.Period))
            .ThenBy(r => r.Rank)
            .Select(r =>
            {
                snapshots.TryGetValue(r.StockId, out var snap);
                return new TopGainerDto(
                    Period: TopGainerPeriodInfo.Key(r.Period),
                    PeriodLabel: TopGainerPeriodInfo.Label(r.Period),
                    PeriodShortLabel: TopGainerPeriodInfo.ShortLabel(r.Period),
                    Rank: r.Rank,
                    Symbol: r.Symbol,
                    Name: r.Name,
                    ReturnPct: r.ReturnPct,
                    StartPrice: r.StartPrice,
                    EndPrice: r.EndPrice,
                    StartDate: r.StartDate,
                    EndDate: r.EndDate,
                    LastClose: snap?.LastClose,
                    PreviousClose: snap?.PreviousClose,
                    Sparkline: snap?.Sparkline,
                    BistIndices: request.MarketType == MarketType.Bist
                        ? BistIndexCompositionSeed.GetIndicesForSymbol(r.Symbol)
                        : null);
            })
            .ToList();

        return new TopGainersResponseDto(
            rows.Max(r => r.EndDate),
            rows.Max(r => r.ComputedAt),
            items);
    }
}
