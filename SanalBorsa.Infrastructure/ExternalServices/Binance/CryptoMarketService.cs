using Microsoft.Extensions.Caching.Memory;
using SanalBorsa.Application.Common.Interfaces;

namespace SanalBorsa.Infrastructure.ExternalServices.Binance;

public sealed class CryptoMarketService : ICryptoMarketService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(5);

    private readonly IBinanceMarketClient _binance;
    private readonly IMemoryCache _cache;
    private readonly ICryptoLiveTickerStore _live;

    public CryptoMarketService(
        IBinanceMarketClient binance,
        IMemoryCache cache,
        ICryptoLiveTickerStore live)
    {
        _binance = binance;
        _cache = cache;
        _live = live;
    }

    public IReadOnlyList<string> GetTrackedSymbols()
    {
        var allowed = _live.GetAllowedSymbols();
        if (allowed.Count > 0) return allowed;
        return _live.GetTracked().Select(t => t.Symbol).ToList();
    }

    public async Task<IReadOnlyList<CryptoTickerDto>> GetTickersAsync(CancellationToken ct = default)
    {
        var map = await GetTickerMapAsync(ct);
        return map.Values
            .OrderByDescending(t => t.QuoteVolume24h)
            .ThenBy(t => t.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<CryptoTickerDto?> GetTickerAsync(string symbol, CancellationToken ct = default)
    {
        var sym = Normalize(symbol);
        EnsureAllowed(sym);
        var map = await GetTickerMapAsync(ct);
        return map.TryGetValue(sym, out var t) ? t : null;
    }

    public async Task<CryptoDepthDto> GetDepthAsync(string symbol, CancellationToken ct = default)
    {
        var sym = Normalize(symbol);
        EnsureAllowed(sym);

        var cacheKey = $"crypto:depth:{sym}";
        if (_cache.TryGetValue(cacheKey, out CryptoDepthDto? cached) && cached is not null)
            return cached;

        var book = await _binance.GetDepthAsync(sym, limit: 20, ct);
        var dto = new CryptoDepthDto(
            sym,
            book.Bids.Select(l => new CryptoDepthLevelDto(l.Price, l.Quantity)).ToList(),
            book.Asks.Select(l => new CryptoDepthLevelDto(l.Price, l.Quantity)).ToList());

        _cache.Set(cacheKey, dto, CacheTtl);
        return dto;
    }

    public async Task<CryptoFillPreviewDto> PreviewBuyAsync(
        string symbol, decimal? quoteUsd, decimal? quantity, CancellationToken ct = default)
    {
        if ((quoteUsd is null or <= 0) && (quantity is null or <= 0))
            throw new InvalidOperationException("quoteUsd veya quantity gerekli.");
        if (quoteUsd is > 0 && quantity is > 0)
            throw new InvalidOperationException("quoteUsd ve quantity aynı anda verilemez.");

        var depth = await GetDepthAsync(symbol, ct);
        return MatchBuy(depth, quoteUsd, quantity);
    }

    public async Task<CryptoFillPreviewDto> PreviewSellAsync(
        string symbol, decimal quantity, CancellationToken ct = default)
    {
        if (quantity <= 0)
            throw new InvalidOperationException("quantity 0'dan büyük olmalıdır.");

        var depth = await GetDepthAsync(symbol, ct);
        return MatchSell(depth, quantity);
    }

    public static CryptoFillPreviewDto MatchBuy(CryptoDepthDto depth, decimal? quoteUsd, decimal? quantity)
    {
        var levels = new List<CryptoFillLevelDto>();
        decimal filledQty = 0;
        decimal total = 0;
        var remainingQuote = quoteUsd ?? 0;
        var remainingQty = quantity ?? 0;
        var byQuote = quoteUsd is > 0;

        foreach (var ask in depth.Asks)
        {
            if (byQuote)
            {
                if (remainingQuote <= 0) break;
                var maxQty = remainingQuote / ask.Price;
                var take = Math.Min(ask.Quantity, maxQty);
                if (take <= 0) break;
                var cost = take * ask.Price;
                levels.Add(new CryptoFillLevelDto(ask.Price, take, cost));
                filledQty += take;
                total += cost;
                remainingQuote -= cost;
            }
            else
            {
                if (remainingQty <= 0) break;
                var take = Math.Min(ask.Quantity, remainingQty);
                if (take <= 0) break;
                var cost = take * ask.Price;
                levels.Add(new CryptoFillLevelDto(ask.Price, take, cost));
                filledQty += take;
                total += cost;
                remainingQty -= take;
            }
        }

        var fully = byQuote
            ? total >= quoteUsd!.Value * 0.999m && filledQty > 0
            : remainingQty <= 0.00000001m;

        var avg = filledQty > 0 ? total / filledQty : 0;
        return new CryptoFillPreviewDto(depth.Symbol, "buy", filledQty, avg, total, fully, levels);
    }

    public static CryptoFillPreviewDto MatchSell(CryptoDepthDto depth, decimal quantity)
    {
        var levels = new List<CryptoFillLevelDto>();
        decimal filledQty = 0;
        decimal total = 0;
        var remaining = quantity;

        foreach (var bid in depth.Bids)
        {
            if (remaining <= 0) break;
            var take = Math.Min(bid.Quantity, remaining);
            if (take <= 0) break;
            var proceeds = take * bid.Price;
            levels.Add(new CryptoFillLevelDto(bid.Price, take, proceeds));
            filledQty += take;
            total += proceeds;
            remaining -= take;
        }

        var fully = remaining <= 0.00000001m;
        var avg = filledQty > 0 ? total / filledQty : 0;
        return new CryptoFillPreviewDto(depth.Symbol, "sell", filledQty, avg, total, fully, levels);
    }

    private async Task<Dictionary<string, CryptoTickerDto>> GetTickerMapAsync(CancellationToken ct)
    {
        if (_live.HasData)
        {
            return _live.GetTracked()
                .ToDictionary(t => t.Symbol, t => t, StringComparer.OrdinalIgnoreCase);
        }

        const string cacheKey = "crypto:tickers";
        if (_cache.TryGetValue(cacheKey, out Dictionary<string, CryptoTickerDto>? cached) && cached is not null)
            return cached;

        var filters = await _binance.GetPriceFiltersAsync(ct);
        _live.SetAllowedSymbols(filters.Keys.ToList());
        _live.SetPriceDecimals(filters.ToDictionary(
            kv => kv.Key, kv => kv.Value.PriceDecimals, StringComparer.OrdinalIgnoreCase));
        _live.SetBaseAssets(filters.ToDictionary(
            kv => kv.Key, kv => kv.Value.BaseAsset, StringComparer.OrdinalIgnoreCase));

        var all = await _binance.GetTickers24hrAsync(ct);
        var map = all
            .Where(t => filters.ContainsKey(t.Symbol))
            .ToDictionary(
                t => t.Symbol,
                t => new CryptoTickerDto(
                    t.Symbol,
                    _live.GetBaseAsset(t.Symbol),
                    t.LastPrice,
                    t.PriceChangePercent,
                    t.QuoteVolume,
                    t.HighPrice,
                    t.LowPrice,
                    _live.GetPriceDecimals(t.Symbol)),
                StringComparer.OrdinalIgnoreCase);

        foreach (var kv in map)
            _live.Upsert(kv.Value);

        _cache.Set(cacheKey, map, CacheTtl);
        return map;
    }

    private static string Normalize(string symbol)
    {
        var s = symbol.Trim().ToUpperInvariant();
        if (!s.EndsWith("USDT", StringComparison.Ordinal))
            s += "USDT";
        return s;
    }

    private void EnsureAllowed(string symbol)
    {
        if (!_live.IsAllowed(symbol))
            throw new InvalidOperationException($"{symbol} desteklenen kripto listesinde değil.");
    }
}
