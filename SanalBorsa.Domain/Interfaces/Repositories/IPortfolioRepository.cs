using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Domain.Interfaces.Repositories;

public interface IPortfolioRepository : IRepository<UserPortfolio>
{
    /// <summary>Portföy + holdings (işlem geçmişi dahil değil).</summary>
    Task<UserPortfolio?> GetByUserIdAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Tek bir portföyü Holdings dahil, Id ile getirir (kurumsal olay uygulama işi için).</summary>
    Task<UserPortfolio?> GetByIdWithHoldingsAsync(Guid portfolioId, CancellationToken ct = default);

    /// <summary>
    /// İşlem geçmişi sayfalı. En yeni önce.
    /// </summary>
    Task<(IReadOnlyList<PortfolioTransaction> Items, int TotalCount)> GetTransactionsPagedAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken ct = default);

    /// <summary>
    /// Bu sembol için (MarketType ile) hiç işlemi olmuş TÜM portföy Id'leri — sadece ŞU AN elinde
    /// tutanlar değil, çünkü ex-date'te tutup sonra satmış olan biri bile o olaya hak kazanmış olabilir.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetPortfolioIdsWithSymbolHistoryAsync(
        string symbol,
        MarketType market,
        CancellationToken ct = default);

    /// <summary>
    /// Bir portföyün, verilen UTC zaman damgasına (dahil) kadarki alım/satım işlemlerinden net elde
    /// tuttuğu miktar (Buy toplamı - Sell toplamı) — kurumsal olay hak sahipliği kontrolü için.
    /// cutoffUtc, çağıran tarafından hesaplanmış KESİN bir UTC anıdır (takvim günü değil) — proje
    /// sohbeti: BIST sanal seansımız 19:00–09:30 TR açık ve hep ÖNCEKİ günün kapanış fiyatıyla
    /// işlem görüyor, o yüzden "gün" bazlı bir sınır yanlış olurdu; doğru sınır
    /// ActionDate'in KENDİ günü, saat 09:30 TR (bkz. ApplyCorporateActionsToPortfoliosCommandHandler).
    /// </summary>
    Task<decimal> GetNetQuantityAsOfAsync(
        Guid portfolioId,
        string symbol,
        MarketType market,
        DateTime cutoffUtc,
        CancellationToken ct = default);
}
