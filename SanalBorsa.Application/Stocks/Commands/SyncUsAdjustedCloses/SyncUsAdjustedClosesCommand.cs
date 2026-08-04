using MediatR;

namespace SanalBorsa.Application.Stocks.Commands.SyncUsAdjustedCloses;

/// <summary>
/// TradingView düzeltilmiş kapanışları (split + temettü dahil toplam getiri) çeker; mevcut fiyat
/// satırlarında yalnızca <c>AdjustedClose</c> güncellenir (<c>Close</c> / OHLCV dokunulmaz).
/// BIST'teki SyncBistAdjustedClosesCommand ile aynı desen.
/// </summary>
public record SyncUsAdjustedClosesCommand(
    string? Symbol = null,
    int? LookbackDays = null)
    : IRequest<SyncUsAdjustedClosesResult>;

public record SyncUsAdjustedClosesResult(
    int Attempted,
    int Synced,
    int RowsUpdated,
    int Failed,
    string? Error);
