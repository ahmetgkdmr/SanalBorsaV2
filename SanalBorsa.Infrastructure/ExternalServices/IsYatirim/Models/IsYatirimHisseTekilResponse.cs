using System.Text.Json.Serialization;

namespace SanalBorsa.Infrastructure.ExternalServices.IsYatirim.Models;

public class IsYatirimHisseTekilResponse
{
    [JsonPropertyName("value")]
    public List<IsYatirimPriceRow>? Value { get; set; }
}

public class IsYatirimPriceRow
{
    [JsonPropertyName("HGDG_HS_KODU")]
    public string? Symbol { get; set; }

    [JsonPropertyName("HGDG_TARIH")]
    public string? Date { get; set; }

    [JsonPropertyName("HGDG_KAPANIS")]
    public decimal? Close { get; set; }

    [JsonPropertyName("HGDG_MIN")]
    public decimal? Low { get; set; }

    [JsonPropertyName("HGDG_MAX")]
    public decimal? High { get; set; }

    [JsonPropertyName("HGDG_HACIM")]
    public decimal? VolumeTl { get; set; }
}
