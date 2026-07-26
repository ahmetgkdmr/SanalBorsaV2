namespace SanalBorsa.Application.Common.Interfaces;

public interface IZorinaqBitcoinArchiveClient
{
    /// <summary>
    /// Zorinaq all-time BTC/USD close serisi (bublina mirror datapoints).
    /// Placeholder / sıfır fiyatlar filtrelenir.
    /// </summary>
    Task<IReadOnlyList<ZorinaqDailyClose>> GetDailyClosesAsync(CancellationToken ct = default);
}

public sealed record ZorinaqDailyClose(DateTime Date, decimal Close);
