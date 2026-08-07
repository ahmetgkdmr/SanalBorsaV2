using AutoMapper;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using SanalBorsa.Application.Common;
using SanalBorsa.Application.Common.Models;
using SanalBorsa.Application.Common.Seeds;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Enums;
using SanalBorsa.Domain.Interfaces;
using SanalBorsa.Domain.Models;

namespace SanalBorsa.Application.Stocks.Queries.GetAllStocks;

public class GetAllStocksQueryHandler : IRequestHandler<GetAllStocksQuery, PagedResult<StockDto>>
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;
    private readonly IMemoryCache _cache;
    private readonly MarketDataCacheVersion _cacheVersion;

    public GetAllStocksQueryHandler(
        IUnitOfWork uow,
        IMapper mapper,
        IMemoryCache cache,
        MarketDataCacheVersion cacheVersion)
    {
        _uow = uow;
        _mapper = mapper;
        _cache = cache;
        _cacheVersion = cacheVersion;
    }

    public async Task<PagedResult<StockDto>> Handle(GetAllStocksQuery request, CancellationToken cancellationToken)
    {
        var cacheKey = $"stocks:bist:v{_cacheVersion.Bist}:{request.Page}:{request.PageSize}:" +
            $"{request.Search}:{request.IsActive}:{request.IndexFilter}:{request.SortBy}:{request.SortDesc}";

        if (_cache.TryGetValue(cacheKey, out PagedResult<StockDto>? cached) && cached is not null)
            return cached;

        var result = await ComputeAsync(request, cancellationToken);
        _cache.Set(cacheKey, result, CacheTtl);
        return result;
    }

    private async Task<PagedResult<StockDto>> ComputeAsync(GetAllStocksQuery request, CancellationToken cancellationToken)
    {
        var all = await _uow.Stocks.GetAllAsync(cancellationToken);
        var topGainers = await _uow.TopGainers.GetByMarketAsync(MarketType.Bist, cancellationToken);
        var championBySymbol = topGainers
            .GroupBy(t => t.Symbol)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Period).First());

        IEnumerable<Stock> filtered = all
            .Where(s => s.MarketType == MarketType.Bist)
            .Where(s => !MarketInstrumentSeed.IsMarketInstrument(s.Exchange));

        if (request.IsActive.HasValue)
            filtered = filtered.Where(s => s.IsActive == request.IsActive.Value);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // Sadece hisse kodunda (sembol) ara — şirket tam ismini de dahil etmek kısa aramalarda
            // (ör. tek harf) alakasız onlarca sonuç üretiyordu (ör. "K" → isminde "Bankası" geçen
            // her şirket).
            var search = request.Search.Trim().ToUpperInvariant();
            filtered = filtered.Where(s =>
                s.Symbol.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var hasIndexFilter = !string.IsNullOrWhiteSpace(request.IndexFilter) &&
            !request.IndexFilter.Equals("all", StringComparison.OrdinalIgnoreCase);

        if (hasIndexFilter)
        {
            filtered = filtered.Where(s =>
                BistIndexCompositionSeed.SymbolMatchesFilter(s.Symbol, request.IndexFilter!));
        }

        var list = filtered.ToList();
        var sortBy = NormalizeSort(request.SortBy);
        var desc = request.SortDesc;

        // Hacim / fiyat / değişim için son gün snapshot'ları gerekir (sıralama şampiyon önceliğinden bağımsız)
        IReadOnlyDictionary<int, MarketPriceSnapshot>? sortSnaps = null;
        if (sortBy is "volume" or "price" or "change")
        {
            sortSnaps = await _uow.PriceHistories.GetMarketSnapshotsAsync(
                list.Select(s => s.Id).ToList(),
                sparklineDays: 2,
                cancellationToken,
                windowDays: 14);
        }

        var ordered = SortStocks(list, sortBy, desc, sortSnaps).ToList();
        var total = ordered.Count;
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize < 1 ? 50 : Math.Min(request.PageSize, 500);

        var pageItems = ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var pageIds = pageItems.Select(s => s.Id).ToList();
        var snapshots = await _uow.PriceHistories.GetMarketSnapshotsAsync(
            pageIds,
            sparklineDays: 28,
            cancellationToken);
        var intradaySparklines = await _uow.IntradayBars.GetSparklinesByStockIdsAsync(pageIds, cancellationToken);

        var items = pageItems
            .Select(stock =>
            {
                var dto = _mapper.Map<StockDto>(stock);
                var bistIndices = BistIndexCompositionSeed.GetIndicesForSymbol(stock.Symbol);
                championBySymbol.TryGetValue(stock.Symbol, out var crown);

                string? period = null;
                string? label = null;
                decimal? ret = null;
                if (crown is not null)
                {
                    period = TopGainerPeriodInfo.Key(crown.Period);
                    label = TopGainerPeriodInfo.Label(crown.Period);
                    ret = crown.ReturnPct;
                }

                if (!snapshots.TryGetValue(stock.Id, out var snap))
                {
                    return dto with
                    {
                        BistIndices = bistIndices,
                        TopGainerPeriod = period,
                        TopGainerLabel = label,
                        TopGainerReturnPct = ret,
                    };
                }

                // Intraday sparkline sadece dünün açılış→kapanış hareketini taşır; badge'deki
                // değişim yüzdesi ÖNCEKİ günün kapanışına göre hesaplandığı için (bkz. BuildSnapshot),
                // grafiğin başına o referans noktasını eklemezsek gap-up/gap-down günlerinde grafik
                // yönü badge'in yönüyle çelişebilir (ör. badge yeşil ama grafik düşen görünür).
                var sparkline = intradaySparklines.TryGetValue(stock.Id, out var intraday) && intraday.Count > 0
                    ? SparklineHelper.PrependPreviousClose(intraday, snap.PreviousClose)
                    : snap.Sparkline;

                return dto with
                {
                    LastClose = snap.LastClose,
                    LastOpen = snap.LastOpen,
                    PreviousClose = snap.PreviousClose,
                    LastVolume = snap.LastVolume,
                    Sparkline = sparkline,
                    BistIndices = bistIndices,
                    TopGainerPeriod = period,
                    TopGainerLabel = label,
                    TopGainerReturnPct = ret,
                };
            })
            .ToList();

        return new PagedResult<StockDto>(items, total, page, pageSize);
    }

    private static string NormalizeSort(string? sortBy)
    {
        var key = (sortBy ?? "volume").Trim().ToLowerInvariant();
        return key is "volume" or "price" or "change" or "name" ? key : "volume";
    }

    private static IEnumerable<Stock> SortStocks(
        List<Stock> list,
        string sortBy,
        bool desc,
        IReadOnlyDictionary<int, MarketPriceSnapshot>? snaps)
    {
        snaps ??= new Dictionary<int, MarketPriceSnapshot>();

        return sortBy switch
        {
            "name" => desc
                ? list.OrderByDescending(s => s.Symbol, StringComparer.OrdinalIgnoreCase)
                : list.OrderBy(s => s.Symbol, StringComparer.OrdinalIgnoreCase),

            "price" => OrderByValue(
                list,
                s => snaps.TryGetValue(s.Id, out var snap) ? snap.LastClose ?? 0m : 0m,
                desc),

            "change" => OrderByValue(list, s => ChangePct(snaps, s.Id), desc),

            _ => OrderByValue(
                list,
                s => snaps.TryGetValue(s.Id, out var snap) ? snap.LastVolume ?? 0L : 0L,
                desc),
        };
    }

    private static IOrderedEnumerable<Stock> OrderByValue<T>(
        List<Stock> list,
        Func<Stock, T> key,
        bool desc) where T : IComparable<T>
    {
        return desc
            ? list.OrderByDescending(key).ThenBy(s => s.Symbol, StringComparer.OrdinalIgnoreCase)
            : list.OrderBy(key).ThenBy(s => s.Symbol, StringComparer.OrdinalIgnoreCase);
    }

    private static decimal ChangePct(IReadOnlyDictionary<int, MarketPriceSnapshot> snaps, int stockId)
    {
        if (!snaps.TryGetValue(stockId, out var snap))
            return 0m;
        if (snap.LastClose is null || snap.PreviousClose is null or 0m)
            return 0m;
        return (snap.LastClose.Value - snap.PreviousClose.Value) / snap.PreviousClose.Value * 100m;
    }
}
