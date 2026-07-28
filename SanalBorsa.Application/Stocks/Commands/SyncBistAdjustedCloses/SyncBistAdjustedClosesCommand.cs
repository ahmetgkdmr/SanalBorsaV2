using MediatR;

namespace SanalBorsa.Application.Stocks.Commands.SyncBistAdjustedCloses;

/// <summary>
/// TradingView düzeltilmiş kapanışları çeker; mevcut fiyat satırlarında yalnızca
/// <c>AdjustedClose</c> güncellenir (<c>Close</c> / OHLCV dokunulmaz).
/// </summary>
public record SyncBistAdjustedClosesCommand(
    bool Full = false,
    string? Symbol = null,
    int? LookbackDays = null)
    : IRequest<SyncBistAdjustedClosesResult>;

public record SyncBistAdjustedClosesResult(
    int Attempted,
    int Synced,
    int RowsUpdated,
    int Failed,
    string? Error);
