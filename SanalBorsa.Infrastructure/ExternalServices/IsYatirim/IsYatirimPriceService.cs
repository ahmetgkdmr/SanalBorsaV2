using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Infrastructure.ExternalServices.IsYatirim.Models;

namespace SanalBorsa.Infrastructure.ExternalServices.IsYatirim;

public class IsYatirimPriceService : IIsYatirimPriceService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<IsYatirimPriceService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public IsYatirimPriceService(
        IHttpClientFactory httpClientFactory,
        ILogger<IsYatirimPriceService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<StockPriceHistory>> GetPriceHistoryAsync(
        string bistSymbol,
        DateTime from,
        DateTime to,
        CancellationToken ct = default)
    {
        var startDate = from.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        var endDate = to.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);

        var url = $"_layouts/15/Isyatirim.Website/Common/Data.aspx/HisseTekil" +
                  $"?hisse={Uri.EscapeDataString(bistSymbol)}" +
                  $"&startdate={startDate}&enddate={endDate}";

        var client = _httpClientFactory.CreateClient("IsYatirim");

        try
        {
            var response = await client.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var data = JsonSerializer.Deserialize<IsYatirimHisseTekilResponse>(json, JsonOptions);

            if (data?.Value is null || data.Value.Count == 0)
            {
                _logger.LogWarning("No İş Yatırım price data for {Symbol}", bistSymbol);
                return [];
            }

            var rows = data.Value
                .Where(r => r.Close is not null && r.High is not null && r.Low is not null && r.Date is not null)
                .OrderBy(r => ParseDate(r.Date!))
                .ToList();

            var records = new List<StockPriceHistory>();
            decimal? previousClose = null;

            foreach (var row in rows)
            {
                var close = Math.Round(row.Close!.Value, 4);
                var high = Math.Round(row.High!.Value, 4);
                var low = Math.Round(row.Low!.Value, 4);
                var open = previousClose ?? close;

                records.Add(new StockPriceHistory
                {
                    Date = ParseDate(row.Date!),
                    Open = open,
                    High = high,
                    Low = low,
                    Close = close,
                    AdjustedClose = close,
                    Volume = (long)Math.Round(row.VolumeTl ?? 0),
                    CreatedAt = DateTime.UtcNow
                });

                previousClose = close;
            }

            _logger.LogInformation(
                "İş Yatırım returned {Count} price records for {Symbol}",
                records.Count, bistSymbol);

            return records;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error fetching İş Yatırım prices for {Symbol}", bistSymbol);
            return [];
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON parse error for İş Yatırım prices of {Symbol}", bistSymbol);
            return [];
        }
    }

    private static DateTime ParseDate(string value)
        => DateTime.ParseExact(value, "dd-MM-yyyy", CultureInfo.InvariantCulture);
}
