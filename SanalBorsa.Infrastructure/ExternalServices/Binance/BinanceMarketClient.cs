using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;

namespace SanalBorsa.Infrastructure.ExternalServices.Binance;

public sealed class BinanceMarketClient : IBinanceMarketClient
{
    private static readonly string[] BaseUrls =
    [
        "https://api.binance.com",
        "https://data-api.binance.vision",
    ];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BinanceMarketClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public BinanceMarketClient(IHttpClientFactory httpClientFactory, ILogger<BinanceMarketClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BinanceTicker24hr>> GetTickers24hrAsync(CancellationToken ct = default)
    {
        var raw = await GetAsync<List<BinanceTickerRaw>>("/api/v3/ticker/24hr", ct)
            ?? [];

        return raw
            .Where(t => t.Symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase)
                || string.Equals(t.Symbol, "USDTTRY", StringComparison.OrdinalIgnoreCase)
                || string.Equals(t.Symbol, "PAXGTRY", StringComparison.OrdinalIgnoreCase))
            .Select(t => new BinanceTicker24hr(
                t.Symbol.ToUpperInvariant(),
                ParseDec(t.LastPrice),
                ParseDec(t.PriceChangePercent),
                ParseDec(t.QuoteVolume),
                ParseDec(t.HighPrice),
                ParseDec(t.LowPrice)))
            .ToList();
    }

    public async Task<BinanceOrderBook> GetDepthAsync(string symbol, int limit = 20, CancellationToken ct = default)
    {
        var sym = symbol.ToUpperInvariant();
        var raw = await GetAsync<BinanceDepthRaw>($"/api/v3/depth?symbol={sym}&limit={limit}", ct)
            ?? throw new InvalidOperationException($"{sym} için derinlik alınamadı.");

        return new BinanceOrderBook(
            sym,
            ParseLevels(raw.Bids),
            ParseLevels(raw.Asks));
    }

    public async Task<IReadOnlyDictionary<string, BinancePriceFilter>> GetPriceFiltersAsync(
        CancellationToken ct = default)
    {
        var raw = await GetAsync<BinanceExchangeInfoRaw>("/api/v3/exchangeInfo", ct)
            ?? new BinanceExchangeInfoRaw();

        var map = new Dictionary<string, BinancePriceFilter>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in raw.Symbols ?? [])
        {
            if (!string.Equals(s.Status, "TRADING", StringComparison.OrdinalIgnoreCase))
                continue;
            var isUsdtQuoted = string.Equals(s.QuoteAsset, "USDT", StringComparison.OrdinalIgnoreCase)
                || s.Symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase);
            // Portföyün anlık USD/TRY kuru için: USDT/TRY paritesi (stablecoin ≈ USD).
            var isUsdtTry = string.Equals(s.BaseAsset, "USDT", StringComparison.OrdinalIgnoreCase)
                && string.Equals(s.QuoteAsset, "TRY", StringComparison.OrdinalIgnoreCase);
            // Canlı gram altın/TRY göstergesi için: PAXG/TRY (1 PAXG ≈ 1 ons altın).
            var isGoldTry = string.Equals(s.BaseAsset, "PAXG", StringComparison.OrdinalIgnoreCase)
                && string.Equals(s.QuoteAsset, "TRY", StringComparison.OrdinalIgnoreCase);
            if (!isUsdtQuoted && !isUsdtTry && !isGoldTry)
                continue;
            if (s.IsSpotTradingAllowed == false)
                continue;

            var symbol = s.Symbol.ToUpperInvariant();

            var tick = s.Filters?
                .FirstOrDefault(f => string.Equals(f.FilterType, "PRICE_FILTER", StringComparison.OrdinalIgnoreCase))
                ?.TickSize;
            var tickSize = ParseDec(tick);
            if (tickSize <= 0) continue;

            var decimals = CountDecimals(tick ?? "0");
            var baseAsset = string.IsNullOrWhiteSpace(s.BaseAsset)
                ? (symbol.EndsWith("USDT", StringComparison.Ordinal) ? symbol[..^4] : symbol)
                : s.BaseAsset.ToUpperInvariant();

            map[symbol] = new BinancePriceFilter(symbol, baseAsset, tickSize, decimals);
        }

        return map;
    }

    public async Task<IReadOnlyList<BinanceKline>> GetDailyKlinesAsync(
        string symbol,
        DateTime? startUtc = null,
        DateTime? endUtc = null,
        CancellationToken ct = default)
    {
        var sym = symbol.Trim().ToUpperInvariant();
        var results = new List<BinanceKline>();
        var cursor = startUtc?.ToUniversalTime() ?? DateTime.UnixEpoch;

        while (!ct.IsCancellationRequested)
        {
            var startMs = new DateTimeOffset(cursor, TimeSpan.Zero).ToUnixTimeMilliseconds();
            var path = $"/api/v3/klines?symbol={sym}&interval=1d&limit=1000&startTime={startMs}";
            if (endUtc.HasValue)
            {
                var endMs = new DateTimeOffset(endUtc.Value.ToUniversalTime(), TimeSpan.Zero).ToUnixTimeMilliseconds();
                path += $"&endTime={endMs}";
            }

            var batch = await GetKlineBatchAsync(path, ct);
            if (batch.Count == 0) break;

            results.AddRange(batch);
            var lastOpen = batch[^1].OpenTimeUtc;
            var next = lastOpen.AddDays(1);
            if (next <= cursor) break;
            cursor = next;

            if (batch.Count < 1000) break;
            if (endUtc.HasValue && cursor > endUtc.Value.ToUniversalTime()) break;

            await Task.Delay(80, ct);
        }

        return results
            .GroupBy(k => k.OpenTimeUtc.Date)
            .Select(g => g.Last())
            .OrderBy(k => k.OpenTimeUtc)
            .ToList();
    }

    public async Task<DateTime?> GetFirstDailyKlineDateAsync(string symbol, CancellationToken ct = default)
    {
        var sym = symbol.Trim().ToUpperInvariant();
        var path = $"/api/v3/klines?symbol={sym}&interval=1d&limit=1&startTime=0";
        try
        {
            var batch = await GetKlineBatchAsync(path, ct);
            return batch.Count == 0 ? null : batch[0].OpenTimeUtc.Date;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Binance first kline failed for {Symbol}", sym);
            return null;
        }
    }

    private async Task<List<BinanceKline>> GetKlineBatchAsync(string pathAndQuery, CancellationToken ct)
    {
        Exception? last = null;
        var client = _httpClientFactory.CreateClient("Binance");

        foreach (var baseUrl in BaseUrls)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl + pathAndQuery);
                using var response = await client.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Binance klines {Url} returned {Status}",
                        baseUrl + pathAndQuery,
                        (int)response.StatusCode);
                    last = new HttpRequestException($"Binance HTTP {(int)response.StatusCode}");
                    continue;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    return [];

                var list = new List<BinanceKline>();
                foreach (var row in doc.RootElement.EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 6) continue;
                    var openMs = row[0].GetInt64();
                    var open = ParseDec(row[1].GetString());
                    var high = ParseDec(row[2].GetString());
                    var low = ParseDec(row[3].GetString());
                    var close = ParseDec(row[4].GetString());
                    var volume = ParseDec(row[5].GetString());
                    if (close <= 0) continue;

                    list.Add(new BinanceKline(
                        DateTimeOffset.FromUnixTimeMilliseconds(openMs).UtcDateTime,
                        open,
                        high,
                        low,
                        close,
                        volume));
                }

                return list;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Binance klines failed via {Base}", baseUrl);
                last = ex;
            }
        }

        throw last ?? new InvalidOperationException("Binance klines isteği başarısız.");
    }

    private async Task<T?> GetAsync<T>(string pathAndQuery, CancellationToken ct)
    {
        Exception? last = null;
        var client = _httpClientFactory.CreateClient("Binance");

        foreach (var baseUrl in BaseUrls)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl + pathAndQuery);
                using var response = await client.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Binance {Url} returned {Status}",
                        baseUrl + pathAndQuery,
                        (int)response.StatusCode);
                    last = new HttpRequestException($"Binance HTTP {(int)response.StatusCode}");
                    continue;
                }

                return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Binance request failed via {Base}", baseUrl);
                last = ex;
            }
        }

        throw last ?? new InvalidOperationException("Binance isteği başarısız.");
    }

    private static IReadOnlyList<BinanceDepthLevel> ParseLevels(List<List<string>>? levels)
    {
        if (levels is null) return [];
        return levels
            .Where(l => l.Count >= 2)
            .Select(l => new BinanceDepthLevel(ParseDec(l[0]), ParseDec(l[1])))
            .ToList();
    }

    private static decimal ParseDec(string? s) =>
        decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0m;

    /// <summary>"0.00100000" → 3</summary>
    private static int CountDecimals(string tickSize)
    {
        var s = tickSize.Trim().TrimEnd('0');
        var dot = s.IndexOf('.');
        if (dot < 0) return 0;
        return Math.Clamp(s.Length - dot - 1, 0, 12);
    }

    private sealed class BinanceTickerRaw
    {
        public string Symbol { get; set; } = "";
        public string LastPrice { get; set; } = "0";
        public string PriceChangePercent { get; set; } = "0";
        public string QuoteVolume { get; set; } = "0";
        public string HighPrice { get; set; } = "0";
        public string LowPrice { get; set; } = "0";
    }

    private sealed class BinanceDepthRaw
    {
        [JsonPropertyName("bids")]
        public List<List<string>>? Bids { get; set; }

        [JsonPropertyName("asks")]
        public List<List<string>>? Asks { get; set; }
    }

    private sealed class BinanceExchangeInfoRaw
    {
        public List<BinanceSymbolRaw>? Symbols { get; set; }
    }

    private sealed class BinanceSymbolRaw
    {
        public string Symbol { get; set; } = "";
        public string Status { get; set; } = "";
        public string BaseAsset { get; set; } = "";
        public string QuoteAsset { get; set; } = "";
        public bool? IsSpotTradingAllowed { get; set; }
        public List<BinanceFilterRaw>? Filters { get; set; }
    }

    private sealed class BinanceFilterRaw
    {
        public string FilterType { get; set; } = "";
        public string? TickSize { get; set; }
    }
}
