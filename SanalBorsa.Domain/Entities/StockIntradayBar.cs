namespace SanalBorsa.Domain.Entities;

/// <summary>
/// Ana ekran sparkline'ı için önceki tam seans gününün 15 dakikalık bar'ları.
/// Geçmişe dönük saklama YOK — her gece ilgili piyasa kapandıktan sonra market bazında
/// tamamen silinip yeniden doldurulur (bkz. RefreshIntradaySparklineCommand).
/// </summary>
public class StockIntradayBar
{
    public long Id { get; set; }

    public int StockId { get; set; }

    public Stock Stock { get; set; } = null!;

    public DateTime BarTime { get; set; }

    public decimal Close { get; set; }
}
