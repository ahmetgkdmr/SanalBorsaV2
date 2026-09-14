using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Application.Mappings;

/// <summary>
/// Entity → DTO dönüşümleri. Daha önce AutoMapper (12.0.1) ile yapılıyordu; o sürümde yüksek
/// önem dereceli bir güvenlik açığı var (GHSA-rvv3-g6hj-g44x) ve 14.x sonrası ticari lisansa
/// geçti. Toplam üç basit dönüşüm için bir bağımlılık taşımak yerine açık uzantı metotları
/// kullanılıyor: derleme zamanında doğrulanıyor, adım adım izlenebiliyor ve DTO'ya yeni alan
/// eklendiğinde sessizce null bırakmak yerine derleyici hatası veriyor.
/// </summary>
public static class EntityMappingExtensions
{
    /// <summary>
    /// Fiyat/sparkline/endeks gibi alanlar burada doldurulmaz — çağıran handler'lar bunları
    /// kendi veri kaynaklarından `with` ifadesiyle ekliyor (AutoMapper profilindeki davranışın
    /// aynısı).
    /// </summary>
    public static StockDto ToDto(this Stock stock) => new(
        stock.Id,
        stock.Symbol,
        stock.Name,
        stock.Sector,
        stock.Industry,
        stock.Currency,
        stock.Exchange,
        stock.IsActive,
        stock.EarliestDataDate,
        stock.LatestDataDate,
        stock.NeedsHistoryRefresh,
        stock.MarketType == MarketType.Crypto ? "crypto" : "bist");

    public static PriceHistoryDto ToDto(this StockPriceHistory price) => new(
        price.Date,
        price.Open,
        price.High,
        price.Low,
        price.Close,
        price.AdjustedClose,
        price.Volume);

    public static CorporateActionDto ToDto(this CorporateAction action) => new(
        action.Id,
        action.Stock?.Symbol ?? string.Empty,
        action.ActionType,
        action.ActionType.ToString(),
        action.ActionDate,
        action.Value,
        action.SubscriptionPrice,
        action.Description,
        action.CreatedAt);
}
