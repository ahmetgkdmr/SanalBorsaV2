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
    string? Error);

public record SyncParityHistoryResult(IReadOnlyList<ParitySyncDetail> Details);
