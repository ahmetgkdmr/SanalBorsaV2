using MediatR;
using SanalBorsa.Application.Common.Interfaces;

namespace SanalBorsa.Application.Crypto.Queries.GetCryptoTickerStrip;

/// <summary>
/// Header kayan şeridi için sabit coin listesi. Sıra gün boyunca değişmez;
/// UTC gün dönümünde hacme göre yeniden belirlenir.
/// </summary>
public record GetCryptoTickerStripQuery(int Count = 20) : IRequest<IReadOnlyList<CryptoStripItemDto>>;

public record CryptoStripItemDto(string Symbol, string BaseAsset, int PriceDecimals);

public class GetCryptoTickerStripQueryHandler
    : IRequestHandler<GetCryptoTickerStripQuery, IReadOnlyList<CryptoStripItemDto>>
{
    /// <summary>Sabit kur/stablecoin çiftleri: şeritte hep ~1,00 gösterdikleri için elenir.</summary>
    private static readonly HashSet<string> Stablecoins = new(StringComparer.OrdinalIgnoreCase)
    {
        "USDC", "FDUSD", "TUSD", "BUSD", "USDP", "DAI", "USD1", "USDE", "USDS", "USDG",
        "PYUSD", "RLUSD", "XUSD", "EURI", "AEUR", "EURC", "EUR", "GBP", "JPY", "TRY", "BRL",
    };

    private static readonly object CacheLock = new();
    private static DateTime _cachedDay;
    private static int _cachedCount;
    private static IReadOnlyList<CryptoStripItemDto>? _cached;

    private readonly ICryptoMarketService _crypto;

    public GetCryptoTickerStripQueryHandler(ICryptoMarketService crypto) => _crypto = crypto;

    public async Task<IReadOnlyList<CryptoStripItemDto>> Handle(
        GetCryptoTickerStripQuery request, CancellationToken cancellationToken)
    {
        var count = Math.Clamp(request.Count, 5, 40);
        var today = DateTime.UtcNow.Date;

        lock (CacheLock)
        {
            if (_cached is { Count: > 0 } && _cachedDay == today && _cachedCount == count)
                return _cached;
        }

        var tickers = await _crypto.GetTickersAsync(cancellationToken);

        var list = tickers
            .Where(t => !Stablecoins.Contains(t.BaseAsset))
            .OrderByDescending(t => t.QuoteVolume24h)
            .Take(count)
            .Select(t => new CryptoStripItemDto(t.Symbol, t.BaseAsset, t.PriceDecimals))
            .ToList();

        if (list.Count == 0) return list;

        lock (CacheLock)
        {
            _cached = list;
            _cachedDay = today;
            _cachedCount = count;
        }

        return list;
    }
}
