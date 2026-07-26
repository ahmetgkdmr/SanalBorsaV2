using MediatR;

namespace SanalBorsa.Application.Crypto.Commands.BackfillCryptoPreBinanceHistory;

/// <summary>
/// Aktif crypto'lar için Binance listing öncesini Yahoo/Coinbase/(BTC:Zorinaq) USD ile doldurur.
/// Binance aralığına dokunmaz. symbol boşsa tüm crypto.
/// </summary>
public record BackfillCryptoPreBinanceHistoryCommand(
    string? Symbol = null
) : IRequest<BackfillCryptoPreBinanceHistoryResult>;

public record BackfillCryptoPreBinanceHistoryResult(
    int SymbolsProcessed,
    int BarsInserted,
    string? Error);
