namespace SanalBorsa.Application.Common.Services;

/// <summary>
/// Yahoo'nun kısa borsa kodunu (chart meta "exchangeName") TradingView'in sembol önekine çevirir
/// — örn. "NMS" (Nasdaq Global Select) → "NASDAQ", "NYQ" → "NYSE". Ham fiyat artık TradingView'dan
/// çekildiği için (bkz. SyncUsDailyPricesCommandHandler) doğru önek olmadan sembol çözülemez.
/// </summary>
public static class UsExchangeResolver
{
    public static string ToTvPrefix(string? yahooExchangeCode) => yahooExchangeCode switch
    {
        "NMS" or "NGM" or "NCM" => "NASDAQ",
        "NYQ" => "NYSE",
        "ASE" => "AMEX",
        "PCX" or "ARCX" => "AMEX",
        "BATS" or "BTS" => "BATS",
        _ => "NASDAQ", // bilinmeyen kod için en yaygın S&P 500 borsası varsayılır
    };

    /// <summary>
    /// Pay sınıfı sembolleri (BRK.B, BF.B gibi) DB'de Yahoo'nun tire kuralına göre saklanıyor
    /// (BRK-B) — ama TradingView bunu nokta ile bekliyor (BRK.B), tireyle sembolü çözemiyor
    /// ("bar gelmedi" hatası). Tam TV sembolü ("EXCHANGE:SYMBOL") kurarken bu çevrimi uygula.
    /// </summary>
    public static string ToTvSymbol(string exchange, string symbol)
        => $"{exchange}:{symbol.Replace('-', '.')}";
}
