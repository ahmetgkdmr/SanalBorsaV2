using MediatR;

namespace SanalBorsa.Application.Crypto.Commands.SyncCryptoHistory;

/// <summary>
/// USDT spot sembollerini Stocks'a seed eder ve Binance günlük kline geçmişini yazar.
/// </summary>
public record SyncCryptoHistoryCommand(
    string? Symbol = null,
    bool FullRefresh = false
) : IRequest<SyncCryptoHistoryResult>;

public record SyncCryptoHistoryResult(
    int SymbolsSeeded,
    int SymbolsSynced,
    int BarsUpserted,
    string? Error);
