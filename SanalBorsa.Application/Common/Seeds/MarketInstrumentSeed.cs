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
        new("XU100", "XU100.IS", "BIST 100 Endeksi", "BIST 100", "INDEX", "TRY", 2),
        new("XU030", "XU030.IS", "BIST 30 Endeksi", "BIST 30", "INDEX", "TRY", 2),
        new("XU050", "XU050.IS", "BIST 50 Endeksi", "BIST 50", "INDEX", "TRY", 2),
        new("XBANK", "XBANK.IS", "BIST Banka Endeksi", "BIST BANKA", "INDEX", "TRY", 2),
        new("USDTRY", "TRY=X", "USD/TRY Döviz Kuru", "USD/TRY", "FX", "TRY", 4),
    ];

    public static bool IsMarketInstrument(string? exchange)
        => exchange is "INDEX" or "FX";

    public static MarketInstrumentEntry? FindBySymbol(string symbol)
        => All.FirstOrDefault(e =>
            e.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));
}
