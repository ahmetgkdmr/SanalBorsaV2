using SanalBorsa.Application.Common.Interfaces;

namespace SanalBorsa.Application.Common.Interfaces;

/// <summary>Binance WS'ten gelen canlı ticker snapshot.</summary>
public interface ICryptoLiveTickerStore
{
    bool HasData { get; }

    void Upsert(CryptoTickerDto ticker);

    /// <summary>Allowed-symbols filtresini atlayıp her zaman yazar — FX quote'ları (USDTRY/EURTRY/
    /// GRAMALTIN gibi TradingView kaynaklı, Binance bootstrap'inin allowed-set'ine dahil olmayan
    /// semboller) için. Binance'in SetAllowedSymbols çağrısı bu semboli asla silmez.</summary>
    void UpsertAlways(CryptoTickerDto ticker);

    CryptoTickerDto? Get(string symbol);

    /// <summary>İzinli USDT spot semboller (volume azalan).</summary>
    IReadOnlyList<CryptoTickerDto> GetTracked();

    void SetAllowedSymbols(IReadOnlyCollection<string> symbols);

    bool IsAllowed(string symbol);

    IReadOnlyList<string> GetAllowedSymbols();

    void SetPriceDecimals(IReadOnlyDictionary<string, int> decimalsBySymbol);

    void SetBaseAssets(IReadOnlyDictionary<string, string> baseBySymbol);

    string GetBaseAsset(string symbol);

    int GetPriceDecimals(string symbol);
}

public interface ICryptoTickerPublisher
{
    Task PublishAsync(CryptoTickerDto ticker, CancellationToken ct = default);
}
