using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;

namespace SanalBorsa.Infrastructure.ExternalServices.Coinbase;

public sealed class CoinbaseMarketClient : ICoinbaseMarketClient
{
    private const int MaxCandlesPerRequest = 300;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CoinbaseMarketClient> _logger;

    public CoinbaseMarketClient(
        IHttpClientFactory httpClientFactory,
        ILogger<CoinbaseMarketClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CoinbaseDailyBar>> GetDailyUsdCandlesAsync(
        string productId,
        DateTime fromUtc,
        DateTime toUtcExclusive,
        CancellationToken ct = default)
    {
        var from = DateTime.SpecifyKind(fromUtc.Date, DateTimeKind.Utc);
        var end = DateTime.SpecifyKind(toUtcExclusive.Date, DateTimeKind.Utc);
        if (end <= from) return [];

        var client = _httpClientFactory.CreateClient("Coinbase");
        var byDate = new Dictionary<DateTime, CoinbaseDailyBar>();

        // İleri doğru sayfalama; her istekte en fazla ~300 gün
        var cursor = from;
        while (cursor < end)
        {
            ct.ThrowIfCancellationRequested();
            var windowEnd = cursor.AddDays(MaxCandlesPerRequest);
            if (windowEnd > end) windowEnd = end;

            var url =
                $"products/{Uri.EscapeDataString(productId)}/candles" +
                $"?granularity=86400" +
                $"&start={Uri.EscapeDataString(cursor.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}" +
                $"&end={Uri.EscapeDataString(windowEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}";

            try
            {
                using var response = await client.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Coinbase {Product} HTTP {Status} ({From:yyyy-MM-dd}→{To:yyyy-MM-dd})",
                        productId, (int)response.StatusCode, cursor, windowEnd);
                    break;
                }

                var rows = await response.Content.ReadFromJsonAsync<JsonElement[]>(cancellationToken: ct);
                if (rows is null || rows.Length == 0)
                {
                    cursor = windowEnd;
                    continue;
                }

                foreach (var row in rows)
                {
                    if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 6)
                        continue;

                    var ts = row[0].GetInt64();
                    var low = GetDec(row[1]);
                    var high = GetDec(row[2]);
                    var open = GetDec(row[3]);
                    var close = GetDec(row[4]);
                    var vol = GetDec(row[5]);
                    if (close <= 0) continue;

                    var date = DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime.Date;
                    if (date < from.Date || date >= end.Date) continue;

                    byDate[date] = new CoinbaseDailyBar(
                        date,
                        open > 0 ? open : close,
                        high > 0 ? high : close,
                        low > 0 ? low : close,
                        close,
                        vol < 0 ? 0 : vol);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
            {
                _logger.LogWarning(ex, "Coinbase candles failed for {Product}", productId);
                break;
            }

            cursor = windowEnd;
            await Task.Delay(120, ct); // nazik throttle
        }

        return byDate.Values.OrderBy(b => b.Date).ToList();
    }

    public async Task<IReadOnlySet<string>> GetUsdProductIdsAsync(CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient("Coinbase");
        try
        {
            using var response = await client.GetAsync("products", ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Coinbase products HTTP {Status}", (int)response.StatusCode);
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var rows = await response.Content.ReadFromJsonAsync<JsonElement[]>(cancellationToken: ct);
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (rows is null) return set;

            foreach (var row in rows)
            {
                if (!row.TryGetProperty("id", out var idEl)) continue;
                var id = idEl.GetString();
                if (string.IsNullOrWhiteSpace(id)) continue;

                var quote = row.TryGetProperty("quote_currency", out var q) ? q.GetString() : null;
                var status = row.TryGetProperty("status", out var s) ? s.GetString() : null;
                if (!string.Equals(quote, "USD", StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrWhiteSpace(status) &&
                    !string.Equals(status, "online", StringComparison.OrdinalIgnoreCase))
                    continue;

                set.Add(id);
            }

            _logger.LogInformation("Coinbase USD products cached: {Count}", set.Count);
            return set;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Coinbase products list failed");
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static decimal GetDec(JsonElement el) =>
        el.ValueKind switch
        {
            JsonValueKind.Number => el.TryGetDecimal(out var d) ? d : (decimal)el.GetDouble(),
            JsonValueKind.String when decimal.TryParse(
                el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var s) => s,
            _ => 0m,
        };
}
