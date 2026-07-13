namespace SanalBorsa.Application.Common.Seeds;

/// <summary>
/// BIST endeks bileşen listeleri. Çeyreklik rebalancing sonrası güncellenebilir.
/// 2025 Q3 verileri esas alınmıştır.
/// </summary>
public static class BistIndexCompositionSeed
{
    // ── Büyüklük endeksleri ───────────────────────────────────────────────────────

    private static readonly HashSet<string> _xu030 = new(StringComparer.OrdinalIgnoreCase)
    {
        "AKBNK","AKSEN","ARCLK","ASELS","ASTOR","BIMAS","DOHOL","EKGYO","ENKAI","EREGL",
        "FROTO","GARAN","HEKTS","ISCTR","KCHOL","KOZAL","KRDMD","MGROS","PETKM","PGSUS",
        "SAHOL","SASA","SISE","TCELL","THYAO","TOASO","TTKOM","TUPRS","VESTL","YKBNK"
    };

    private static readonly HashSet<string> _xu050Extra = new(StringComparer.OrdinalIgnoreCase)
    {
        "AEFES","AGHOL","ALARK","BRSAN","CCOLA","CIMSA","DOAS","EGEEN","GUBRF","ISGYO",
        "KARSN","KONTR","LOGO","MAVI","ODAS","OYAKC","SMRTG","TAVHL","ULKER","ZOREN"
    };

    private static readonly HashSet<string> _xu100Extra = new(StringComparer.OrdinalIgnoreCase)
    {
        "ADEL","AFYON","AGESA","AKCNS","AKENR","AKFGY","AKGRT","ALKA","ANACM","AYGAZ",
        "BAGFS","BJKAS","BOLUC","BSOKE","BTCIM","CEMTS","CLEBI","CWENE","DEVA","DMSAS",
        "FENER","GESAN","GLYHO","GSRAY","HALKB","INDES","IPEKE","ISYAT","KAREL","KARTN",
        "KLNMA","KOZAA","KTLEV","MAALT","NETAS","NUHCM","PRKME","RZGYO","SKBNK",
        "TABGD","TRGYO","TSKB","TTRAK","TUKAS","VAKBN","VESBE","YATAS","ZRGYO"
    };

    // ── Sektör endeksleri ─────────────────────────────────────────────────────────

    private static readonly HashSet<string> _xbank = new(StringComparer.OrdinalIgnoreCase)
    {
        "AKBNK","ALBRK","DENIZ","GARAN","HALKB","ICBCT","ISBTR","ISCTR","KLNMA","KTURN",
        "ODEAB","QNBTR","SKBNK","TSKB","TURSG","VAKBN","YKBNK"
    };

    private static readonly HashSet<string> _xutek = new(StringComparer.OrdinalIgnoreCase)
    {
        "ARCLK","ASELS","FONET","INDES","ISYAT","KAREL","LOGO","NETAS","PKART","TCELL",
        "TTKOM","VESBE","VBTYZ","MNDTR","INPOL","ARENA"
    };

    private static readonly HashSet<string> _xusin = new(StringComparer.OrdinalIgnoreCase)
    {
        "ARCLK","BRSAN","DOAS","EGEEN","EREGL","FROTO","GESAN","KARSN","KRDMD","OYAKC",
        "SASA","TOASO","TTRAK","VESTL","ASELS","BOLUC","BTCIM","CIMSA","NUHCM","BSOKE",
        "DMSAS","ANACM","AKCNS","CEMTS","ADEL"
    };

    private static readonly HashSet<string> _xuhiz = new(StringComparer.OrdinalIgnoreCase)
    {
        "AEFES","BIMAS","CCOLA","CLEBI","MGROS","PGSUS","TAVHL","THYAO","ULKER","DARDL",
        "PENGD","TATGD","KNFRT"
    };

    private static readonly HashSet<string> _xumal = new(StringComparer.OrdinalIgnoreCase)
    {
        "AKBNK","ALBRK","DOHOL","EKGYO","GARAN","HALKB","ISCTR","ISGYO","KLNMA","KCHOL",
        "KTLEV","RZGYO","SAHOL","SKBNK","TSKB","TRGYO","VAKBN","YKBNK","ZRGYO","AGESA",
        "AKFGY","AKGRT","AGHOL","DENIZ","ODEAB","QNBTR","TURSG","GLYHO"
    };

    private static readonly HashSet<string> _xgida = new(StringComparer.OrdinalIgnoreCase)
    {
        "AEFES","CCOLA","DARDL","KNFRT","MGROS","PENGD","TATGD","TUKAS","ULKER","BIMAS",
        "TABGD","SELVA","EMKEL"
    };

    private static readonly HashSet<string> _xkmya = new(StringComparer.OrdinalIgnoreCase)
    {
        "BAGFS","DEVA","GUBRF","HEKTS","IPEKE","PETKM","SASA","TUPRS","ALKIN","KCAER"
    };

    private static readonly HashSet<string> _xelkt = new(StringComparer.OrdinalIgnoreCase)
    {
        "AKENR","AKSEN","CWENE","ENJSA","EUPWR","MAGEN","ODAS","ZOREN","ENKAI"
    };

    private static readonly HashSet<string> _xtast = new(StringComparer.OrdinalIgnoreCase)
    {
        "ANACM","BOLUC","BSOKE","BTCIM","CIMSA","KARSN","NUHCM","ADANA","AFYON","AKCNS"
    };

    private static readonly HashSet<string> _xmana = new(StringComparer.OrdinalIgnoreCase)
    {
        "EREGL","IPEKE","KRDMD","KOZAL","KOZAA","MAALT","PRKME","BRSAN","EGEEN"
    };

    private static readonly HashSet<string> _xspor = new(StringComparer.OrdinalIgnoreCase)
    {
        "BJKAS","FENER","GSRAY","TSPOR","AFJET"
    };

    private static readonly HashSet<string> _xktum = new(StringComparer.OrdinalIgnoreCase)
    {
        "AKENR","ALARK","ASELS","ASTOR","BTCIM","CEMTS","CWENE","EKGYO","ENKAI","EREGL",
        "FROTO","GUBRF","HEKTS","ISGYO","KARTN","KCHOL","KRDMD","LOGO","MAVI","ODAS",
        "OYAKC","PGSUS","SAHOL","SISE","TAVHL","THYAO","TOASO","VESTL","ZOREN","BRSAN",
        "DOAS","EGEEN","KARSN","SASA","TTRAK","AKCNS","BTCIM","CWENE","NUHCM"
    };

    private static readonly HashSet<string> _xkury = new(StringComparer.OrdinalIgnoreCase)
    {
        "AKBNK","ARCLK","ASELS","BIMAS","EKGYO","ENKAI","EREGL","FROTO","GARAN","ISCTR",
        "KCHOL","LOGO","SAHOL","SISE","TCELL","THYAO","TOASO","TUPRS","YKBNK","KRDMD",
        "MAVI","TTKOM","VESTL","DOHL","SASA","HEKTS","PETKM","MGROS","TAVHL"
    };

    // ── Birleşik kümeler ──────────────────────────────────────────────────────────

    private static readonly HashSet<string> _allXu050;
    private static readonly HashSet<string> _allXu100;
    private static readonly Dictionary<string, HashSet<string>> _indexMap;

    static BistIndexCompositionSeed()
    {
        _allXu050 = new HashSet<string>(_xu030.Concat(_xu050Extra), StringComparer.OrdinalIgnoreCase);
        _allXu100 = new HashSet<string>(_allXu050.Concat(_xu100Extra), StringComparer.OrdinalIgnoreCase);

        _indexMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["XU030"] = _xu030,
            ["XU050"] = _allXu050,
            ["XU100"] = _allXu100,
            ["XBANK"] = _xbank,
            ["XUTEK"] = _xutek,
            ["XUSIN"] = _xusin,
            ["XUHIZ"] = _xuhiz,
            ["XUMAL"] = _xumal,
            ["XGIDA"] = _xgida,
            ["XKMYA"] = _xkmya,
            ["XELKT"] = _xelkt,
            ["XTAST"] = _xtast,
            ["XMANA"] = _xmana,
            ["XSPOR"] = _xspor,
            ["XKTUM"] = _xktum,
            ["XKURY"] = _xkury,
        };
    }

    /// <summary>Verilen sembolün ait olduğu tüm endeks sembollerini döner.</summary>
    public static IReadOnlyList<string> GetIndicesForSymbol(string symbol)
    {
        var result = new List<string>(4);
        foreach (var (key, set) in _indexMap)
            if (set.Contains(symbol))
                result.Add(key);
        return result;
    }

    /// <summary>Verilen endeks filtresine giren sembollerin hash setini döner. Bilinmeyen endeks için null.</summary>
    public static HashSet<string>? GetSymbolsForIndex(string indexSymbol)
        => _indexMap.TryGetValue(indexSymbol, out var set) ? set : null;

    /// <summary>Verilen sembolün endeks filtresiyle eşleşip eşleşmediğini kontrol eder.</summary>
    public static bool SymbolMatchesFilter(string symbol, string indexFilter)
    {
        if (string.IsNullOrWhiteSpace(indexFilter) || indexFilter.Equals("all", StringComparison.OrdinalIgnoreCase))
            return true;
        var set = GetSymbolsForIndex(indexFilter);
        return set?.Contains(symbol) ?? false;
    }
}
