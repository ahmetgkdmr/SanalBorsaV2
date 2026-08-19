using MediatR;

namespace SanalBorsa.Application.Stocks.Commands.SyncBistAdjustedCloses;

/// <summary>
/// TradingView düzeltilmiş kapanışları çeker; mevcut fiyat satırlarında yalnızca
/// <c>AdjustedClose</c> güncellenir (<c>Close</c> / OHLCV dokunulmaz).
/// </summary>
public record SyncBistAdjustedClosesCommand(
    bool Full = false,
    string? Symbol = null,
    int? LookbackDays = null,
    /// <summary>0 = ilk deneme, retry zincirindeyse deneme sırası (sadece loglama amaçlı —
    /// retry zamanlamasına AdjustedCloseRetryJob karar verir, bkz. AnomalyRetryPolicy).</summary>
    int RetryAttempt = 0)
    : IRequest<SyncBistAdjustedClosesResult>;

public record SyncBistAdjustedClosesResult(
    int Attempted,
    int Synced,
    int RowsUpdated,
    int Failed,
    string? Error,
    int Suspicious = 0,
    IReadOnlyList<string>? SuspiciousSymbols = null);
