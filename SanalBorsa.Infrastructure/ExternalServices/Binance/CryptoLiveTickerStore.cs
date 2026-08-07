using System.Collections.Concurrent;
using SanalBorsa.Application.Common.Interfaces;

namespace SanalBorsa.Infrastructure.ExternalServices.Binance;

public sealed class CryptoLiveTickerStore : ICryptoLiveTickerStore
{
    private readonly ConcurrentDictionary<string, CryptoTickerDto> _map =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, int> _decimals =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, string> _baseAssets =
        new(StringComparer.OrdinalIgnoreCase);

    private volatile HashSet<string> _allowed =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Binance'in allowed-set reset'lerinden (SetAllowedSymbols) etkilenmeyen semboller — FX quote'ları.</summary>
    private readonly ConcurrentDictionary<string, byte> _alwaysAllowed =
        new(StringComparer.OrdinalIgnoreCase);

    public bool HasData => !_map.IsEmpty;

    public void Upsert(CryptoTickerDto ticker)
    {
        if (_allowed.Count > 0 && !_allowed.Contains(ticker.Symbol) && !_alwaysAllowed.ContainsKey(ticker.Symbol))
            return;

        UpsertCore(ticker);
    }

    public void UpsertAlways(CryptoTickerDto ticker)
    {
        _alwaysAllowed[ticker.Symbol] = 0;
        UpsertCore(ticker);
    }

    private void UpsertCore(CryptoTickerDto ticker)
    {
        var decimals = GetPriceDecimals(ticker.Symbol);
        var baseAsset = string.IsNullOrWhiteSpace(ticker.BaseAsset)
            ? GetBaseAsset(ticker.Symbol)
            : ticker.BaseAsset;
        _map[ticker.Symbol] = ticker with { PriceDecimals = decimals, BaseAsset = baseAsset };
    }

    public CryptoTickerDto? Get(string symbol) =>
        _map.TryGetValue(symbol, out var t) ? t : null;

    public IReadOnlyList<CryptoTickerDto> GetTracked()
    {
        var allowed = _allowed;
        IEnumerable<CryptoTickerDto> src = _map.Values;
        if (allowed.Count > 0)
            src = src.Where(t => allowed.Contains(t.Symbol) || _alwaysAllowed.ContainsKey(t.Symbol));

        return src
            .OrderByDescending(t => t.QuoteVolume24h)
            .ThenBy(t => t.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void SetAllowedSymbols(IReadOnlyCollection<string> symbols)
    {
        _allowed = new HashSet<string>(
            symbols.Select(s => s.ToUpperInvariant()),
            StringComparer.OrdinalIgnoreCase);

        foreach (var key in _map.Keys)
        {
            if (!_allowed.Contains(key) && !_alwaysAllowed.ContainsKey(key))
                _map.TryRemove(key, out _);
        }
    }

    public bool IsAllowed(string symbol) =>
        _allowed.Count == 0 || _allowed.Contains(symbol);

    public IReadOnlyList<string> GetAllowedSymbols() =>
        _allowed.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();

    public void SetPriceDecimals(IReadOnlyDictionary<string, int> decimalsBySymbol)
    {
        foreach (var (symbol, decimals) in decimalsBySymbol)
            _decimals[symbol] = Math.Clamp(decimals, 0, 12);
    }

    public void SetBaseAssets(IReadOnlyDictionary<string, string> baseBySymbol)
    {
        foreach (var (symbol, baseAsset) in baseBySymbol)
        {
            if (!string.IsNullOrWhiteSpace(baseAsset))
                _baseAssets[symbol] = baseAsset.ToUpperInvariant();
        }
    }

    public string GetBaseAsset(string symbol)
    {
        if (_baseAssets.TryGetValue(symbol, out var b)) return b;
        return symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase)
            ? symbol[..^4]
            : symbol;
    }

    public int GetPriceDecimals(string symbol) =>
        _decimals.TryGetValue(symbol, out var d) ? d : 8;
}
