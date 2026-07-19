using System.Text.Json.Serialization;

namespace SanalBorsa.Infrastructure.ExternalServices.IsYatirim.Models;

/// <summary>ASP.NET WebMethod envelope: { "d": "&lt;json-array-string&gt;" }</summary>
public class IsYatirimWebMethodResponse
{
    [JsonPropertyName("d")]
    public string? D { get; set; }
}

public class IsYatirimSermayeArttirimRow
{
    [JsonPropertyName("HISSE_KODU")]
    public string? Symbol { get; set; }

    [JsonPropertyName("SHHE_TARIH")]
    public long TarihEpochMs { get; set; }

    [JsonPropertyName("SHHE_BDLI_ORAN")]
    public decimal BedelliOranPct { get; set; }

    [JsonPropertyName("SHHE_BDLI_NOM_TUTAR")]
    public decimal BedelliNomTutar { get; set; }

    [JsonPropertyName("SHHE_BDSZ_IK_ORAN")]
    public decimal BedelsizIkOranPct { get; set; }

    [JsonPropertyName("SHHE_BDSZ_TM_ORAN")]
    public decimal BedelsizTemettuOranPct { get; set; }

    [JsonPropertyName("SHHE_NAKIT_TM_ORAN")]
    public decimal NakitTemettuOranPct { get; set; }

    [JsonPropertyName("SHHE_NAKIT_TM_TUTAR")]
    public decimal NakitTemettuTutar { get; set; }

    [JsonPropertyName("SHT_TANIMI")]
    public string? TipTanimi { get; set; }

    [JsonPropertyName("SHHE_ACIKLAMA")]
    public string? Aciklama { get; set; }
}
