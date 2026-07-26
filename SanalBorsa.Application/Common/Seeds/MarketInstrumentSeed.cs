namespace SanalBorsa.Application.Common.Seeds;

public record MarketInstrumentEntry(
    string Symbol,
    string YahooSymbol,
    string Name,
    string DisplayName,
    string Exchange,
    string Currency,
    int DisplayDecimals);

public static class MarketInstrumentSeed
{
    public static readonly IReadOnlyList<MarketInstrumentEntry> All =
    [
        // ── Ana endeksler ────────────────────────────────────────────────
        new("XU100",  "XU100.IS",  "BIST 100 Endeksi",              "BIST 100",        "INDEX", "TRY", 2),
        new("XU030",  "XU030.IS",  "BIST 30 Endeksi",               "BIST 30",         "INDEX", "TRY", 2),
        new("XU050",  "XU050.IS",  "BIST 50 Endeksi",               "BIST 50",         "INDEX", "TRY", 2),
        new("XBANK",  "XBANK.IS",  "BIST Banka Endeksi",            "BIST BANKA",      "INDEX", "TRY", 2),

        // ── Sektör endeksleri ────────────────────────────────────────────
        new("XUTEK",  "XUTEK.IS",  "BIST Teknoloji Endeksi",        "BİST TEKNOLOJİ",  "INDEX", "TRY", 2),
        new("XUSIN",  "XUSIN.IS",  "BIST Sınai Endeksi",            "BİST SINAI",      "INDEX", "TRY", 2),
        new("XUHIZ",  "XUHIZ.IS",  "BIST Hizmetler Endeksi",        "BİST HİZMETLER",  "INDEX", "TRY", 2),
        new("XUMAL",  "XUMAL.IS",  "BIST Mali Endeksi",             "BİST MALİ",       "INDEX", "TRY", 2),
        new("XGIDA",  "XGIDA.IS",  "BIST Gıda İçecek Endeksi",     "BİST GIDA",       "INDEX", "TRY", 2),
        new("XKMYA",  "XKMYA.IS",  "BIST Kimya Petrol Endeksi",     "BİST KİMYA",      "INDEX", "TRY", 2),
        new("XELKT",  "XELKT.IS",  "BIST Elektrik Endeksi",         "BİST ELEKTRİK",   "INDEX", "TRY", 2),
        new("XTAST",  "XTAST.IS",  "BIST Taş Toprak Endeksi",       "BİST TAŞ TOPRAK", "INDEX", "TRY", 2),
        new("XMANA",  "XMANA.IS",  "BIST Mali A.Ş. Endeksi",        "BİST MALİ A.Ş.", "INDEX", "TRY", 2),
        new("XSPOR",  "XSPOR.IS",  "BIST Spor Endeksi",             "BİST SPOR",       "INDEX", "TRY", 2),

        // ── Katılım & Yönetim ────────────────────────────────────────────
        new("XKTUM",  "XKTUM.IS",  "BIST Katılım Endeksi",          "BİST KATILIM",    "INDEX", "TRY", 2),
        new("XKURY",  "XKURY.IS",  "BIST Kurumsal Yönetim Endeksi", "BİST KUR. YÖN.",  "INDEX", "TRY", 2),

        // ── Döviz & değerli maden (TL bazlı pariteler) ───────────────────
        new("USDTRY",    "TRY=X",    "USD/TRY Döviz Kuru",  "USD/TRY",    "FX", "TRY", 4),
        new("EURTRY",    "EURTRY=X", "EUR/TRY Döviz Kuru",  "EUR/TRY",    "FX", "TRY", 4),
        // GC=F / TV XAUUSD × USDTRY ÷ 31,1034768 ile türetilir.
        new("GRAMALTIN", "",         "Gram Altın (TL)",     "GRAM ALTIN", "FX", "TRY", 2),
    ];

    /// <summary>Zaman makinesinde hisseye alternatif gösterilen TL pariteleri (sıra = gösterim sırası).</summary>
    public static readonly IReadOnlyList<string> ParitySymbols = ["USDTRY", "EURTRY", "GRAMALTIN"];

    /// <summary>Yahoo'dan çekilen ons altın kaynağı; TL gram fiyatı buradan türetilir (yedek).</summary>
    public const string GoldOunceYahooSymbol = "GC=F";

    /// <summary>TradingView FX / metal sembolleri.</summary>
    public const string UsdTryTvSymbol = "FX_IDC:USDTRY";
    public const string EurTryTvSymbol = "FX_IDC:EURTRY";
    public const string EurUsdTvSymbol = "FX_IDC:EURUSD";
    public const string XauUsdTvSymbol = "FX_IDC:XAUUSD";

    /// <summary>1 troy ons = 31,1034768 gram.</summary>
    public const decimal GramsPerTroyOunce = 31.1034768m;

    public static bool IsMarketInstrument(string? exchange)
        => exchange is "INDEX" or "FX";

    public static bool IsParitySymbol(string? symbol)
        => symbol is not null
           && ParitySymbols.Any(p => p.Equals(symbol, StringComparison.OrdinalIgnoreCase));

    public static MarketInstrumentEntry? FindBySymbol(string symbol)
        => All.FirstOrDefault(e =>
            e.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));
}
