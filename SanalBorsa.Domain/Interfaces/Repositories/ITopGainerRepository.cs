using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Enums;

namespace SanalBorsa.Domain.Interfaces.Repositories;

public interface ITopGainerRepository
{
    Task<IReadOnlyList<TopGainer>> GetAllAsync(CancellationToken ct = default);

    Task<IReadOnlyList<TopGainer>> GetByMarketAsync(MarketType marketType, CancellationToken ct = default);

    /// <summary>Yalnızca verilen market'in satırlarını silip yeniler (diğer market korunur).</summary>
    Task ReplaceForMarketAsync(
        MarketType marketType,
        IReadOnlyList<TopGainer> rows,
        CancellationToken ct = default);
}
