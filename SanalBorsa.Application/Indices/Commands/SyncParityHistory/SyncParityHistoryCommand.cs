using MediatR;

namespace SanalBorsa.Application.Indices.Commands.SyncParityHistory;

/// <summary>
/// USD/TRY, EUR/TRY ve gram altın (TL) günlük fiyat geçmişini tazeler.
/// <paramref name="Full"/> = true ise seri sıfırdan yeniden çekilir.
/// </summary>
public record SyncParityHistoryCommand(bool Full = false)
    : IRequest<SyncParityHistoryResult>;

public record ParitySyncDetail(
    string Symbol,
    int RowsWritten,
    DateTime? EarliestDate,
    DateTime? LatestDate,
    string? Error,
    /// <summary>
    /// En yeni günün barı öncekinden anormal sapıyordu ve henüz "sonraki gün" ile doğrulanamadığı
    /// için yazılmadı — kaynağın geçici bir sorunu olabilir, ~1 saat sonra tek seferlik tekrar
    /// denenmeli (bkz. TimeMachineLeadersJob).
    /// </summary>
    bool NeedsRetry = false);

public record SyncParityHistoryResult(IReadOnlyList<ParitySyncDetail> Details)
{
    public bool NeedsRetry => Details.Any(d => d.NeedsRetry);
}
