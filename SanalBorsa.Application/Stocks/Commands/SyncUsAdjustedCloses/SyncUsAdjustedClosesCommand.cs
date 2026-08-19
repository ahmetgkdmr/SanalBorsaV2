using MediatR;

namespace SanalBorsa.Application.Stocks.Commands.SyncUsAdjustedCloses;

/// <summary>
/// TradingView düzeltilmiş kapanışları (split + temettü dahil toplam getiri) çeker; mevcut fiyat
/// satırlarında yalnızca <c>AdjustedClose</c> güncellenir (<c>Close</c> / OHLCV dokunulmaz).
/// BIST'teki SyncBistAdjustedClosesCommand ile aynı desen.
/// </summary>
public record SyncUsAdjustedClosesCommand(
    string? Symbol = null,
    int? LookbackDays = null,
    /// <summary>0 = ilk deneme, retry zincirindeyse deneme sırası (sadece loglama amaçlı —
    /// retry zamanlamasına AdjustedCloseRetryJob karar verir, bkz. AnomalyRetryPolicy).</summary>
    int RetryAttempt = 0)
    : IRequest<SyncUsAdjustedClosesResult>;

public record SyncUsAdjustedClosesResult(
    int Attempted,
    int Synced,
    int RowsUpdated,
    int Failed,
    string? Error,
    int Suspicious = 0,
    IReadOnlyList<string>? SuspiciousSymbols = null);
