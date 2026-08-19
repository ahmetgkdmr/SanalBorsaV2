using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Application.Common.Seeds;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SanalBorsa.Infrastructure.ExternalServices.Bist;

/// <summary>
/// KAP'ın CANLI "İhraççı" (İGS) şirket listesinden BIST hisse evrenini çeker — proje sohbeti:
/// önceki sürüm 3. parti bir GitHub JSON aynası kullanıyordu, bu ayna KOZAL→TRALT gibi güncel
/// unvan/kod değişikliklerini yansıtmıyordu. KAP'ın kendi API'si zaten CANLI/güncel, ayrıca ETF/fon
/// gibi hisse-dışı enstrümanları hiç içermiyor (İGS sadece pay senedi ihraç eden şirketler).
/// KAP'ın "stockCode" alanı çoklu pay sınıfı olan şirketlerde TEK bir string içinde virgülle
/// ayrılmış birden fazla kod taşıyabiliyor (ör. "KRDMA, KRDMB, KRDMD") — her biri BIST'te AYRI
/// işlem gören gerçek bir hisse olduğu için her alt-kodu kendi başına bir sembol olarak döndürüyoruz.
/// </summary>
public class KapBistSymbolProvider : IBistSymbolProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<KapBistSymbolProvider> _logger;

    private const string KapCompanyListUrl = "tr/api/company/items/IGS/A";

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
            var client = _httpClientFactory.CreateClient("Kap");
            var response = await client.GetAsync(KapCompanyListUrl, ct);
            response.EnsureSuccessStatusCode();

            var items = await response.Content.ReadFromJsonAsync<List<KapCompanyEntry>>(JsonOptions, ct);
            if (items is null || items.Count == 0)
                return Fallback();

            var symbols = new List<BistSymbolInfo>();
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.StockCode) || string.IsNullOrWhiteSpace(item.Title))
                    continue;

                foreach (var rawCode in item.StockCode.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var code = rawCode.Trim().ToUpperInvariant();
                    if (code.Length == 0) continue;
                    symbols.Add(new BistSymbolInfo(code, item.Title.Trim()));
                }
            }

            var deduped = symbols
                .GroupBy(x => x.Symbol)
                .Select(g => g.First())
                .OrderBy(x => x.Symbol)
                .ToList();

            if (deduped.Count == 0)
                return Fallback();

            _logger.LogInformation("Loaded {Count} BIST symbols from KAP IGS/A (canlı)", deduped.Count);
            return deduped;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load BIST symbols from KAP IGS/A — using static fallback");
            return Fallback();
        }
    }

    private static IReadOnlyList<BistSymbolInfo> Fallback()
        => BistSymbolSeed.Symbols
            .Select(s => new BistSymbolInfo(s, s))
            .ToList();

    private sealed class KapCompanyEntry
    {
        [JsonPropertyName("stockCode")]
        public string? StockCode { get; set; }

        [JsonPropertyName("kapMemberTitle")]
        public string? Title { get; set; }
    }
}
