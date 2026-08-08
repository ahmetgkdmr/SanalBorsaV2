using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Enums;

namespace SanalBorsa.Domain.Interfaces.Repositories;

public interface ITimeMachineLeaderRepository
{
    /// <summary>
    /// Verilen tarihe eşit ya da ondan önceki en yakın işlem gününün satırları
    /// (tatil / hafta sonu seçilirse otomatik olarak geriye kayar).
    /// </summary>
    Task<IReadOnlyList<TimeMachineLeader>> GetForDateAsync(
        TimeMachineCategory category,
        DateTime onOrBefore,
        CancellationToken ct = default);

    /// <summary>
    /// Aynı gün için hem kazananları (Rank 1..topCount) hem kaybedenleri (Rank -1..-bottomCount)
    /// tek seferde döndürür — "günün zenginlik testi" raporu için.
    /// </summary>
    Task<IReadOnlyList<TimeMachineLeader>> GetTopAndBottomForDateAsync(
        TimeMachineCategory category,
        DateTime onOrBefore,
        int topCount,
        int bottomCount,
        CancellationToken ct = default);

    /// <summary>Kategori satırlarını komple değiştirir (silip toplu yazar).</summary>
    Task ReplaceCategoryAsync(
        TimeMachineCategory category,
        IReadOnlyList<TimeMachineLeader> rows,
        CancellationToken ct = default);

    Task<IReadOnlyList<TimeMachineLeaderStats>> GetStatsAsync(CancellationToken ct = default);
}

public record TimeMachineLeaderStats(
    TimeMachineCategory Category,
    int Rows,
    DateTime? EarliestStartDate,
    DateTime? LatestStartDate,
    DateTime? EndDate,
    DateTime? ComputedAt);
