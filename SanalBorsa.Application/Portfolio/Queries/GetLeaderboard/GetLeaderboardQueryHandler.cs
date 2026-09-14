using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Portfolio.Queries.GetLeaderboard;

/// <summary>
/// Sıralamayı canlı fiyatlarla hesaplar.
///
/// <para>
/// Maliyet kontrolü: tüm portföyler tek sorguda (kullanıcı + holdings dahil), fiyatlar tek
/// toplu sorguda çekilir — kullanıcı başına sorgu (N+1) yok. Sonuç kısa süreli önbelleğe
/// alınır; sıralama saniyede bir değişmesi gereken bir veri değil, ama sayfa her açıldığında
/// bütün portföyleri yeniden fiyatlamak da gereksiz.
/// </para>
/// </summary>
public class GetLeaderboardQueryHandler : IRequestHandler<GetLeaderboardQuery, LeaderboardDto>
{
    /// <summary>Herkesin başlangıç sermayesi — getiri yüzdesi buna göre hesaplanır.</summary>
    private const decimal StartingCashTry = 1_000_000m;

    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);
    private const string CacheKey = "leaderboard";

    private readonly IUnitOfWork _uow;
    private readonly IPortfolioFxRateProvider _fx;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GetLeaderboardQueryHandler> _logger;

    public GetLeaderboardQueryHandler(
        IUnitOfWork uow,
        IPortfolioFxRateProvider fx,
        IMemoryCache cache,
        ILogger<GetLeaderboardQueryHandler> logger)
    {
        _uow = uow;
        _fx = fx;
        _cache = cache;
        _logger = logger;
    }

    public async Task<LeaderboardDto> Handle(GetLeaderboardQuery request, CancellationToken ct)
    {
        var take = Math.Clamp(request.Take, 1, 200);

        if (_cache.TryGetValue<LeaderboardDto>(CacheKey, out var cached) && cached is not null)
            return Trim(cached, take);

        var full = await ComputeAsync(ct);
        _cache.Set(CacheKey, full, CacheDuration);
        return Trim(full, take);
    }

    private static LeaderboardDto Trim(LeaderboardDto dto, int take)
        => dto with { Entries = dto.Entries.Take(take).ToList() };

    private async Task<LeaderboardDto> ComputeAsync(CancellationToken ct)
    {
        var portfolios = await _uow.Portfolios.GetAllWithUserAndHoldingsAsync(ct);
        if (portfolios.Count == 0)
            return new LeaderboardDto([], DateTime.UtcNow, 0);

        var priceBySymbol = await LoadPricesAsync(portfolios, ct);

        // Dolar bazlı varlıklar (ABD hissesi, kripto) TL'ye çevrilir. Kur alınamazsa sıralamayı
        // yanlış hesaplamaktansa o varlıkları maliyetinden saymak daha dürüst olurdu; ancak
        // kur sağlayıcısı zaten alım/satımda kullanılıyor ve hata durumunda istisna fırlatıyor,
        // bu yüzden burada da aynı davranışı bekliyoruz.
        var usdTry = await _fx.GetUsdTryRateAsync(ct);

        var ranked = portfolios
            .Where(p => p.User is not null)
            .Select(p => new
            {
                Portfolio = p,
                Value = ComputeValue(p, priceBySymbol, usdTry),
            })
            .OrderByDescending(x => x.Value)
            .Select((x, i) => new LeaderboardEntryDto(
                Rank: i + 1,
                Username: x.Portfolio.User.Username,
                DisplayName: string.IsNullOrWhiteSpace(x.Portfolio.User.DisplayName)
                    ? x.Portfolio.User.Username
                    : x.Portfolio.User.DisplayName,
                AvatarUrl: x.Portfolio.User.AvatarUrl,
                PortfolioValue: decimal.Round(x.Value, 2),
                GainPct: decimal.Round((x.Value - StartingCashTry) / StartingCashTry * 100m, 2),
                TradeHistoryPublic: x.Portfolio.User.ShowTradeHistoryPublic,
                HoldingCount: x.Portfolio.Holdings.Count(h => h.Quantity > 0)))
            .ToList();

        _logger.LogInformation("Liderlik tablosu hesaplandı: {Count} katılımcı.", ranked.Count);

        return new LeaderboardDto(ranked, DateTime.UtcNow, ranked.Count);
    }

    /// <summary>
    /// Portföylerde geçen tüm sembollerin son kapanışını tek seferde çeker.
    /// Anahtar (sembol + piyasa) çiftidir: aynı sembol farklı piyasalarda bulunabiliyor.
    /// </summary>
    private async Task<Dictionary<(string Symbol, MarketType Market), decimal>> LoadPricesAsync(
        IReadOnlyList<UserPortfolio> portfolios, CancellationToken ct)
    {
        var needed = portfolios
            .SelectMany(p => p.Holdings)
            .Where(h => h.Quantity > 0)
            .Select(h => (Symbol: h.Symbol, Market: h.MarketType))
            .Distinct()
            .ToList();

        var result = new Dictionary<(string, MarketType), decimal>();
        if (needed.Count == 0) return result;

        foreach (var market in needed.Select(n => n.Market).Distinct())
        {
            var symbols = needed.Where(n => n.Market == market).Select(n => n.Symbol).ToList();
            var stocks = await _uow.Stocks.GetAllActiveAsync(ct, market);

            var idBySymbol = stocks
                .Where(s => s.MarketType == market && symbols.Contains(s.Symbol))
                .ToDictionary(s => s.Id, s => s.Symbol);

            if (idBySymbol.Count == 0) continue;

            var snapshots = await _uow.PriceHistories.GetMarketSnapshotsAsync(
                idBySymbol.Keys.ToList(), sparklineDays: 1, ct: ct);

            foreach (var (stockId, snap) in snapshots)
            {
                if (snap.LastClose is { } price && price > 0 && idBySymbol.TryGetValue(stockId, out var sym))
                    result[(sym, market)] = price;
            }
        }

        return result;
    }

    private static decimal ComputeValue(
        UserPortfolio portfolio,
        Dictionary<(string Symbol, MarketType Market), decimal> prices,
        decimal usdTry)
    {
        var total = portfolio.Cash;

        foreach (var h in portfolio.Holdings)
        {
            if (h.Quantity <= 0) continue;

            // Fiyatı bulunamayan varlık (ör. sembol pasife alınmış) sıfır sayılmaz —
            // kullanıcının sıralaması bizim veri boşluğumuz yüzünden çökmemeli; ortalama
            // maliyetiyle değerlenir.
            var unit = prices.TryGetValue((h.Symbol, h.MarketType), out var p) ? p : h.AvgCost;

            var value = h.Quantity * unit;
            if (h.MarketType is MarketType.Crypto or MarketType.UsStocks)
                value *= usdTry;

            total += value;
        }

        return total;
    }
}
