using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;

namespace SanalBorsa.Infrastructure.ExternalServices.Zorinaq;

/// <summary>
/// https://bitcoin.zorinaq.com/price/ arşivinin community mirror'ı:
/// https://price.bublina.eu.org/datapoints.txt
/// </summary>
public sealed class ZorinaqBitcoinArchiveClient : IZorinaqBitcoinArchiveClient
{
    private static readonly Regex PointRegex = new(
        @"new Date\(""(?<d>\d{4}-\d{2}-\d{2})""\),\s*(?<p>[0-9]*\.?[0-9]+([eE][-+]?\d+)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ZorinaqBitcoinArchiveClient> _logger;

    public ZorinaqBitcoinArchiveClient(
        IHttpClientFactory httpClientFactory,
        ILogger<ZorinaqBitcoinArchiveClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ZorinaqDailyClose>> GetDailyClosesAsync(CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient("ZorinaqArchive");
        try
        {
            var text = await client.GetStringAsync("datapoints.txt", ct);
            var byDate = new Dictionary<DateTime, decimal>();

            foreach (Match m in PointRegex.Matches(text))
            {
                if (!DateTime.TryParseExact(
                        m.Groups["d"].Value, "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var date))
                    continue;

                if (!decimal.TryParse(
                        m.Groups["p"].Value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var price))
                    continue;

                // Chart placeholder'ları (genesis ~0) atla; gerçek kotasyon 2009-10-05
                if (price < 0.0001m) continue;

                byDate[date.Date] = price;
            }

            var list = byDate
                .Select(kv => new ZorinaqDailyClose(kv.Key, kv.Value))
                .OrderBy(x => x.Date)
                .ToList();

            _logger.LogInformation(
                "Zorinaq archive loaded: {Count} daily closes ({From:yyyy-MM-dd} → {To:yyyy-MM-dd})",
                list.Count,
                list.Count > 0 ? list[0].Date : (DateTime?)null,
                list.Count > 0 ? list[^1].Date : (DateTime?)null);

            return list;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Zorinaq archive fetch failed");
            return [];
        }
    }
}
