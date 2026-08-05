using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Application.Common.Interfaces;

/// <summary>TradingView WebSocket günlük geçmiş — BIST veya FX_IDC vb. tam sembol.</summary>
public interface ITradingViewHistoryService
{
    /// <summary><paramref name="bistSymbol"/> örn. THYAO → <c>BIST:THYAO</c>.</summary>
    Task<IReadOnlyList<StockPriceHistory>> GetBistDailyBarsAsync(
        string bistSymbol,
        DateTime from,
        DateTime to,
        CancellationToken ct = default);

    Task<IReadOnlyDictionary<DateTime, decimal>> GetBistAdjustedClosesAsync(
        string bistSymbol,
        DateTime from,
        DateTime to,
        CancellationToken ct = default);

    /// <summary>Tam TV sembolü — örn. <c>FX_IDC:EURTRY</c>, <c>FX_IDC:XAUUSD</c>.</summary>
    Task<IReadOnlyList<StockPriceHistory>> GetDailyBarsByTvSymbolAsync(
        string tvSymbol,
        DateTime from,
        DateTime to,
        CancellationToken ct = default);

    /// <summary>
    /// Tam TV sembolü için düzeltilmiş kapanış (<c>adjustment=dividends</c>) — split + temettü dahil
    /// toplam getiri serisi. Zaman Makinesi hesabı artık olay-bazlı simülasyon yerine bu seri
    /// üzerindeki oranı kullanıyor (bkz. TimeMachineCalculator).
    /// </summary>
    Task<IReadOnlyDictionary<DateTime, decimal>> GetAdjustedClosesByTvSymbolAsync(
        string tvSymbol,
        DateTime from,
        DateTime to,
        CancellationToken ct = default);

    /// <summary>
    /// Son tam seans gününün intraday bar'ları (varsayılan 15 dakika) — ana ekran sparkline'ı için.
    /// Geçmişe dönük değil, sadece TradingView'ın döndürdüğü en güncel seans günü.
    /// </summary>
    Task<IReadOnlyList<(DateTime BarTimeUtc, decimal Close)>> GetIntradayBarsByTvSymbolAsync(
        string tvSymbol,
        string resolution = "15",
        CancellationToken ct = default);
}
