namespace SanalBorsa.Application.Common.Interfaces;

public interface IBistSymbolProvider
{
    /// <summary>
    /// Returns all known BIST ticker symbols with company names.
    /// Primary source: KAP (Public Disclosure Platform) derived list.
    /// </summary>
    Task<IReadOnlyList<BistSymbolInfo>> GetSymbolsAsync(CancellationToken ct = default);
}

public record BistSymbolInfo(string Symbol, string Name);
