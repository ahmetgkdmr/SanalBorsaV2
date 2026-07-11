using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Application.Common.Interfaces;

/// <summary>
/// Fetches BIST daily price history from İş Yatırım (close, min, max — no open).
/// Data is adjusted (split/dividend) per BIST conventions.
/// </summary>
public interface IIsYatirimPriceService
{
    Task<IReadOnlyList<StockPriceHistory>> GetPriceHistoryAsync(
        string bistSymbol,
        DateTime from,
        DateTime to,
        CancellationToken ct = default);
}
