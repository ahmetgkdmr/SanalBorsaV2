using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Domain.Interfaces.Repositories;

public interface IStockIntradayBarRepository
{
    /// <summary>Verilen market'e ait TÜM intraday bar'ları siler (join üzerinden, diğer market'lere dokunmaz).</summary>
    Task DeleteAllByMarketAsync(MarketType market, CancellationToken ct = default);

    Task BulkInsertAsync(IEnumerable<StockIntradayBar> bars, CancellationToken ct = default);

    /// <summary>Sembol → BarTime sıralı Close listesi (sparkline için).</summary>
    Task<IReadOnlyDictionary<int, IReadOnlyList<decimal>>> GetSparklinesByStockIdsAsync(
        IReadOnlyList<int> stockIds,
        CancellationToken ct = default);
}
