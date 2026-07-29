using MediatR;

namespace SanalBorsa.Application.Stocks.Commands.DeactivateInactiveBistStocks;

/// <summary>
/// BIST'te TradingView üzerinden fiyat geçmişi alınamayan (delist / işlem sırası kapalı /
/// çözülemeyen) aktif hisseleri <c>IsActive=false</c> yapar. Fiyat geçmişi silinmez.
/// </summary>
public record DeactivateInactiveBistStocksCommand(int LookbackDays = 60)
    : IRequest<DeactivateInactiveBistStocksResult>;

public record DeactivateInactiveBistStocksResult(
    int Checked,
    int Deactivated,
    int FailedProbe,
    IReadOnlyList<string> DeactivatedSymbols,
    string? Error);
