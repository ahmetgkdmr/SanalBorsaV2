using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Common.Services;

/// <summary>
/// Bizim TradingView kaynaklı DÜZELTİLMİŞ (AdjustedClose) fiyat serimizi, bağımsız bir ikinci
/// kaynak olan Yahoo Finance'in kendi adjclose'uyla gün gün karşılaştırır. Bkz. proje sohbeti:
/// ROK/HUBB'ta TradingView'in geçici olarak bozuk AdjustedClose verdiği, kısa süre sonra
/// kendiliğinden düzeldiği tespit edildi — bu servis, tüm geçmişi tarayıp benzer (belki daha ince,
/// henüz tespit edilmemiş) sapmaları bulmak için kullanılır (bkz. PriceDataAuditJob).
///
/// SADECE düzeltilmiş fiyat karşılaştırılır — ham (Close) karşılaştırması bilerek YOK: Yahoo'nun
/// "close" alanı gerçekte ham değil, split'leri geriye dönük zaten uygulanmış olarak geliyor (bkz.
/// proje sohbeti — ROK'un 1987 split'i Yahoo'nun "close" serisinde hiç görünmüyor, bizim ham
/// serimizde gerçek bir %50 düşüş olarak görünüyor). Ham fiyatın doğruluğu bunun yerine
/// <see cref="RawPriceActionConsistencyService"/> ile KENDİ kurumsal olay kayıtlarımıza karşı
/// denetleniyor — dış bir "ham" kaynağa ihtiyaç duymadan.
/// </summary>
public sealed class PriceDataAuditService
{
    /// <summary>Yahoo/TradingView aynı günü %2'den fazla farklı fiyatlıyorsa şüpheli sayılır.</summary>
    private const decimal DiffThreshold = 0.02m;

    /// <summary>
    /// Varsayılan karşılaştırma penceresi — daha eskiye gidince iki bağımsız temettü-bileşikleme
    /// hesabı (TV'nin kendi "dividends" ayarı vs Yahoo'nun adjclose'u) doğal olarak ıraksıyor
    /// (bkz. proje sohbeti: ROK örneğinde 179 temettü olayında binde birlik farklar bile 40 yılda
    /// %30'a katlanıyor). Bu pencerede iki kaynak yakın kalıyor, gerçek bozukluklar hâlâ net ayırt
    /// edilebiliyor (ROK/HUBB tam da bu aralıkta yakalandı).
    /// </summary>
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromDays(365 * 15);

    private readonly IUnitOfWork _uow;
    private readonly IYahooFinanceService _yahoo;
    private readonly ILogger<PriceDataAuditService> _logger;

    public PriceDataAuditService(
        IUnitOfWork uow, IYahooFinanceService yahoo, ILogger<PriceDataAuditService> logger)
    {
        _uow = uow;
        _yahoo = yahoo;
        _logger = logger;
    }

    /// <param name="since">Karşılaştırmanın başlayacağı tarih — null ise son 15 yıl kullanılır.
    /// Tam geçmişi taramak için (bilerek gürültülü olduğunu kabul ederek) DateTime.UnixEpoch geç.</param>
    public async Task<StockAuditResult> AuditStockAsync(Stock stock, CancellationToken ct, DateTime? since = null)
    {
        if (string.IsNullOrWhiteSpace(stock.YahooSymbol))
            return new StockAuditResult(stock.Symbol, [], NoYahooData: true);

        var from = since ?? DateTime.UtcNow.Date.Subtract(DefaultWindow);

        var yahoo = await _yahoo.TryGetPriceHistoryAsync(stock.YahooSymbol, from, DateTime.UtcNow.Date, ct);
        if (yahoo.Count == 0)
            return new StockAuditResult(stock.Symbol, [], NoYahooData: true);

        var ours = await _uow.PriceHistories.GetByStockIdAsync(stock.Id, from: from, ct: ct);
        var oursByDate = ours
            .GroupBy(p => p.Date.Date)
            .ToDictionary(g => g.Key, g => g.First());

        var adjMismatches = new List<PriceMismatch>();

        foreach (var y in yahoo)
        {
            if (!oursByDate.TryGetValue(y.Date.Date, out var ourBar))
                continue;

            if (Diverges(ourBar.AdjustedClose, y.AdjustedClose, out var adjRatio))
                adjMismatches.Add(new PriceMismatch(y.Date.Date, ourBar.AdjustedClose, y.AdjustedClose, adjRatio));
        }

        return new StockAuditResult(stock.Symbol, adjMismatches, NoYahooData: false);
    }

    private static bool Diverges(decimal ours, decimal theirs, out decimal ratioDiff)
    {
        ratioDiff = 0m;
        if (ours <= 0m || theirs <= 0m) return false;
        ratioDiff = Math.Abs(ours / theirs - 1m);
        return ratioDiff > DiffThreshold;
    }
}

public record PriceMismatch(DateTime Date, decimal Ours, decimal Theirs, decimal RatioDiff);

public record StockAuditResult(
    string Symbol,
    IReadOnlyList<PriceMismatch> AdjustedMismatches,
    bool NoYahooData)
{
    public bool HasMismatches => AdjustedMismatches.Count > 0;
}
