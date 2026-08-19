using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Application.Common;

/// <summary>
/// "Zaman Makinesi" evreninden hariç tutulan stablecoin/fiat bazlı kripto pariteleri — bunlar
/// fiyat değişimi göstermediği için kazanan/kaybeden yarışına hiç girmemeli. Hem liderlik
/// hesaplamasında hem "kaç enstrüman vardı" evren sayımında aynı liste kullanılmalı.
/// </summary>
public static class CryptoStableAssets
{
    private static readonly HashSet<string> Bases = new(StringComparer.OrdinalIgnoreCase)
    {
        "USDC", "FDUSD", "TUSD", "BUSD", "USDP", "DAI", "USD1", "USDE", "USDS", "USDG",
        "PYUSD", "RLUSD", "XUSD", "EURI", "AEUR", "EURC", "EUR", "GBP", "JPY", "TRY", "BRL",
        "USDT",
    };

    public static bool IsStable(Stock stock)
    {
        var baseAsset = !string.IsNullOrWhiteSpace(stock.Name)
            ? stock.Name.Trim().ToUpperInvariant()
            : stock.Symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase)
                ? stock.Symbol[..^4]
                : stock.Symbol;
        return Bases.Contains(baseAsset);
    }
}
