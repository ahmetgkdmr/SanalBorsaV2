using System.Text.Json.Serialization;

namespace SanalBorsa.Infrastructure.ExternalServices.YahooFinance.Models;

public class YahooChartResponse
{
    [JsonPropertyName("chart")]
    public YahooChart? Chart { get; set; }
}

public class YahooChart
{
    [JsonPropertyName("result")]
    public List<YahooChartResult>? Result { get; set; }

    [JsonPropertyName("error")]
    public object? Error { get; set; }
}

public class YahooChartResult
{
    [JsonPropertyName("meta")]
    public YahooMeta? Meta { get; set; }

    [JsonPropertyName("timestamp")]
    public List<long>? Timestamp { get; set; }

    [JsonPropertyName("indicators")]
    public YahooIndicators? Indicators { get; set; }

    [JsonPropertyName("events")]
    public YahooEvents? Events { get; set; }
}

public class YahooMeta
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("longName")]
    public string? LongName { get; set; }

    [JsonPropertyName("shortName")]
    public string? ShortName { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("exchangeName")]
    public string? ExchangeName { get; set; }

    [JsonPropertyName("regularMarketPrice")]
    public decimal? RegularMarketPrice { get; set; }
}

public class YahooIndicators
{
    [JsonPropertyName("quote")]
    public List<YahooQuote>? Quote { get; set; }

    [JsonPropertyName("adjclose")]
    public List<YahooAdjClose>? AdjClose { get; set; }
}

public class YahooQuote
{
    [JsonPropertyName("open")]
    public List<decimal?>? Open { get; set; }

    [JsonPropertyName("high")]
    public List<decimal?>? High { get; set; }

    [JsonPropertyName("low")]
    public List<decimal?>? Low { get; set; }

    [JsonPropertyName("close")]
    public List<decimal?>? Close { get; set; }

    [JsonPropertyName("volume")]
    public List<long?>? Volume { get; set; }
}

public class YahooAdjClose
{
    [JsonPropertyName("adjclose")]
    public List<decimal?>? AdjClose { get; set; }
}

public class YahooEvents
{
    [JsonPropertyName("dividends")]
    public Dictionary<string, YahooDividend>? Dividends { get; set; }

    [JsonPropertyName("splits")]
    public Dictionary<string, YahooSplit>? Splits { get; set; }
}

public class YahooDividend
{
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("date")]
    public long Date { get; set; }
}

public class YahooSplit
{
    [JsonPropertyName("date")]
    public long Date { get; set; }

    [JsonPropertyName("numerator")]
    public decimal Numerator { get; set; }

    [JsonPropertyName("denominator")]
    public decimal Denominator { get; set; }

    [JsonPropertyName("splitRatio")]
    public string? SplitRatio { get; set; }
}
