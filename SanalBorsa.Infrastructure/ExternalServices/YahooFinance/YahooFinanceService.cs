using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Enums;
using SanalBorsa.Infrastructure.ExternalServices.YahooFinance.Models;
using System.Text.Json;

namespace SanalBorsa.Infrastructure.ExternalServices.YahooFinance;

public class YahooFinanceService : IYahooFinanceService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<YahooFinanceService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public YahooFinanceService(IHttpClientFactory httpClientFactory, ILogger<YahooFinanceService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public Task<IReadOnlyList<StockPriceHistory>> GetPriceHistoryAsync(
        string yahooSymbol,
        DateTime from,
        DateTime to,
        CancellationToken ct = default)
        => FetchPriceHistoryAsync(yahooSymbol, from, to, clientName: "YahooFinance", quiet: false, ct);

    public Task<IReadOnlyList<StockPriceHistory>> TryGetPriceHistoryAsync(
        string yahooSymbol,
        DateTime from,
        DateTime to,
        CancellationToken ct = default)
        => FetchPriceHistoryAsync(yahooSymbol, from, to, clientName: "YahooFinanceProbe", quiet: true, ct);

    private async Task<IReadOnlyList<StockPriceHistory>> FetchPriceHistoryAsync(
        string yahooSymbol,
        DateTime from,
        DateTime to,
        string clientName,
        bool quiet,
        CancellationToken ct)
    {
        var period1 = ToUnixTimestamp(from);
        var period2 = ToUnixTimestamp(to);

        var url = $"v8/finance/chart/{Uri.EscapeDataString(yahooSymbol)}" +
                  $"?period1={period1}&period2={period2}&interval=1d&events=div%2Csplits&includeAdjustedClose=true";

        var client = _httpClientFactory.CreateClient(clientName);

        try
        {
            var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                if (!quiet)
                    _logger.LogWarning("Yahoo {Symbol} HTTP {Status}", yahooSymbol, (int)response.StatusCode);
                return [];
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var data = JsonSerializer.Deserialize<YahooChartResponse>(json, JsonOptions);

            var result = data?.Chart?.Result?.FirstOrDefault();
            if (result?.Timestamp is null || result.Indicators?.Quote is null)
            {
                if (!quiet)
                    _logger.LogWarning("No price data returned for {Symbol}", yahooSymbol);
                return [];
            }

            var quote = result.Indicators.Quote[0];
            var adjCloseList = result.Indicators.AdjClose?.FirstOrDefault()?.AdjClose;
            var timestamps = result.Timestamp;

            var records = new List<StockPriceHistory>();

            for (int i = 0; i < timestamps.Count; i++)
            {
                var open = quote.Open?.ElementAtOrDefault(i);
                var high = quote.High?.ElementAtOrDefault(i);
                var low = quote.Low?.ElementAtOrDefault(i);
                var close = quote.Close?.ElementAtOrDefault(i);
                var volume = quote.Volume?.ElementAtOrDefault(i);
                var adjClose = adjCloseList?.ElementAtOrDefault(i);

                if (close is null)
                    continue;

                var openVal = open ?? close.Value;
                var highVal = high ?? close.Value;
                var lowVal = low ?? close.Value;

                var date = DateTimeOffset.FromUnixTimeSeconds(timestamps[i]).UtcDateTime.Date;

                records.Add(new StockPriceHistory
                {
                    Date = date,
                    Open = Math.Round(openVal, 4),
                    High = Math.Round(highVal, 4),
                    Low = Math.Round(lowVal, 4),
                    Close = Math.Round(close.Value, 4),
                    AdjustedClose = Math.Round(adjClose ?? close.Value, 4),
                    Volume = volume ?? 0,
                    CreatedAt = DateTime.UtcNow
                });
            }

            return records
                .GroupBy(r => r.Date)
                .Select(g => g.Last())
                .OrderBy(r => r.Date)
                .ToList();
        }
        catch (HttpRequestException ex)
        {
            if (!quiet)
                _logger.LogError(ex, "HTTP error fetching price history for {Symbol}", yahooSymbol);
            return [];
        }
        catch (JsonException ex)
        {
            if (!quiet)
                _logger.LogError(ex, "JSON parse error for price history of {Symbol}", yahooSymbol);
            return [];
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            if (!quiet)
                _logger.LogWarning("Yahoo timeout for {Symbol}", yahooSymbol);
            return [];
        }
    }

    public async Task<IReadOnlyList<CorporateAction>> GetCorporateActionsAsync(
        string yahooSymbol,
        CancellationToken ct = default)
    {
        // Yahoo Finance returns events (dividends + splits) in the chart endpoint
        var period1 = 0;
        var period2 = ToUnixTimestamp(DateTime.UtcNow);

        var url = $"v8/finance/chart/{Uri.EscapeDataString(yahooSymbol)}" +
                  $"?period1={period1}&period2={period2}&interval=1d&events=div%2Csplits";

        var client = _httpClientFactory.CreateClient("YahooFinance");

        try
        {
            var response = await client.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var data = JsonSerializer.Deserialize<YahooChartResponse>(json, JsonOptions);

            var result = data?.Chart?.Result?.FirstOrDefault();
            if (result is null)
                return [];

            var actions = new List<CorporateAction>();

            // Map Yahoo dividends → CorporateActionType.Dividend
            if (result.Events?.Dividends is not null)
            {
                foreach (var (_, div) in result.Events.Dividends)
                {
                    actions.Add(new CorporateAction
                    {
                        ActionType = CorporateActionType.Dividend,
                        ActionDate = DateTimeOffset.FromUnixTimeSeconds(div.Date).UtcDateTime.Date,
                        Value = Math.Round(div.Amount, 6),
                        Description = $"Cash dividend: {div.Amount:F4} per share",
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            // Map Yahoo splits → CorporateActionType.BonusIssue
            // Yahoo reports a "split" when shares are subdivided; BIST bedelsiz = bonus issue
            if (result.Events?.Splits is not null)
            {
                foreach (var (_, split) in result.Events.Splits)
                {
                    // Ratio stored as numerator/denominator (e.g. 2:1 split → value = 2.0)
                    var ratio = split.Denominator != 0
                        ? Math.Round(split.Numerator / split.Denominator, 6)
                        : split.Numerator;

                    actions.Add(new CorporateAction
                    {
                        ActionType = CorporateActionType.BonusIssue,
                        ActionDate = DateTimeOffset.FromUnixTimeSeconds(split.Date).UtcDateTime.Date,
                        Value = ratio,
                        Description = $"Stock split: {split.SplitRatio ?? $"{split.Numerator}:{split.Denominator}"}",
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            return actions;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error fetching corporate actions for {Symbol}", yahooSymbol);
            return [];
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON parse error for corporate actions of {Symbol}", yahooSymbol);
            return [];
        }
    }

    public async Task<StockMetadata?> GetStockMetadataAsync(string yahooSymbol, CancellationToken ct = default)
    {
        // quoteSummary requires crumb auth; chart meta endpoint works with Referer header
        var url = $"v8/finance/chart/{Uri.EscapeDataString(yahooSymbol)}?interval=1d&range=5d";

        var client = _httpClientFactory.CreateClient("YahooFinance");

        try
        {
            var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync(ct);
            var data = JsonSerializer.Deserialize<YahooChartResponse>(json, JsonOptions);

            var meta = data?.Chart?.Result?.FirstOrDefault()?.Meta;
            if (meta is null)
                return null;

            // BIST sembolleri ".IS" ekiyle gelir (THYAO.IS); ABD sembollerinde ek yok (AAPL).
            var isBist = yahooSymbol.EndsWith(".IS", StringComparison.OrdinalIgnoreCase);
            var localSymbol = isBist ? yahooSymbol[..^3] : yahooSymbol;

            return new StockMetadata(
                Symbol: localSymbol,
                YahooSymbol: yahooSymbol,
                LongName: meta.LongName ?? meta.ShortName ?? localSymbol,
                Sector: null,
                Industry: null,
                Currency: meta.Currency ?? (isBist ? "TRY" : "USD"),
                Exchange: meta.ExchangeName ?? (isBist ? "IST" : "US")
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching metadata for {Symbol}", yahooSymbol);
            return null;
        }
    }

    private static long ToUnixTimestamp(DateTime dt)
        => ((DateTimeOffset)DateTime.SpecifyKind(dt, DateTimeKind.Utc)).ToUnixTimeSeconds();
}
