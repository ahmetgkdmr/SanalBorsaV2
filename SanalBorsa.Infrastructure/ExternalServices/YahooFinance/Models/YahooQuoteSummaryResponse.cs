using System.Text.Json.Serialization;

namespace SanalBorsa.Infrastructure.ExternalServices.YahooFinance.Models;

public class YahooQuoteSummaryResponse
{
    [JsonPropertyName("quoteSummary")]
    public YahooQuoteSummary? QuoteSummary { get; set; }
}

public class YahooQuoteSummary
{
    [JsonPropertyName("result")]
    public List<YahooQuoteSummaryResult>? Result { get; set; }

    [JsonPropertyName("error")]
    public object? Error { get; set; }
}

public class YahooQuoteSummaryResult
{
    [JsonPropertyName("assetProfile")]
    public YahooAssetProfile? AssetProfile { get; set; }

    [JsonPropertyName("price")]
    public YahooPriceModule? Price { get; set; }
}

public class YahooAssetProfile
{
    [JsonPropertyName("sector")]
    public string? Sector { get; set; }

    [JsonPropertyName("industry")]
    public string? Industry { get; set; }

    [JsonPropertyName("longBusinessSummary")]
    public string? LongBusinessSummary { get; set; }
}

public class YahooPriceModule
{
    [JsonPropertyName("longName")]
    public string? LongName { get; set; }

    [JsonPropertyName("shortName")]
    public string? ShortName { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("exchangeName")]
    public string? ExchangeName { get; set; }
}
