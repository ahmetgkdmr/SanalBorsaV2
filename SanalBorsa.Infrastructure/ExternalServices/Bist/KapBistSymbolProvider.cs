using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Application.Common.Seeds;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SanalBorsa.Infrastructure.ExternalServices.Bist;

/// <summary>
/// Fetches the full BIST symbol universe from a KAP-derived public JSON feed.
/// Falls back to the static seed list when the remote source is unavailable.
/// </summary>
public class KapBistSymbolProvider : IBistSymbolProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<KapBistSymbolProvider> _logger;

    private const string KapSymbolListUrl =
        "https://cdn.jsdelivr.net/gh/ahmeterenodaci/Istanbul-Stock-Exchange--BIST--including-symbols-and-logos@main/without_logo.min.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public KapBistSymbolProvider(IHttpClientFactory httpClientFactory, ILogger<KapBistSymbolProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BistSymbolInfo>> GetSymbolsAsync(CancellationToken ct = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("BistSymbols");
            var json = await client.GetStringAsync(KapSymbolListUrl, ct);

            var items = JsonSerializer.Deserialize<List<KapSymbolEntry>>(json, JsonOptions);
            if (items is null || items.Count == 0)
                return Fallback();

            var symbols = items
                .Where(x => !string.IsNullOrWhiteSpace(x.Symbol))
                .Select(x => new BistSymbolInfo(
                    x.Symbol.Trim().ToUpperInvariant(),
                    x.Name?.Trim() ?? x.Symbol.Trim().ToUpperInvariant()))
                .GroupBy(x => x.Symbol)
                .Select(g => g.First())
                .OrderBy(x => x.Symbol)
                .ToList();

            _logger.LogInformation("Loaded {Count} BIST symbols from KAP feed", symbols.Count);
            return symbols;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load BIST symbols from KAP feed — using static fallback");
            return Fallback();
        }
    }

    private static IReadOnlyList<BistSymbolInfo> Fallback()
        => BistSymbolSeed.Symbols
            .Select(s => new BistSymbolInfo(s, s))
            .ToList();

    private sealed class KapSymbolEntry
    {
        [JsonPropertyName("symbol")]
        public string Symbol { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
