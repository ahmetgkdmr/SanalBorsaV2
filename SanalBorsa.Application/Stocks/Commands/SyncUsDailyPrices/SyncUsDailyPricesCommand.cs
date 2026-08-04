using MediatR;

namespace SanalBorsa.Application.Stocks.Commands.SyncUsDailyPrices;

/// <summary>
/// ABD hisseleri (S&amp;P 500 pilotu) için günlük OHLC + AdjustedClose senkronu — Yahoo Finance.
/// <paramref name="LookbackDays"/> verilirse LatestDataDate yerine bugünden geriye o kadar gün çekilir.
/// </summary>
public record SyncUsDailyPricesCommand(
    bool Full = false,
    string? Symbol = null,
    int? LookbackDays = null)
    : IRequest<SyncUsDailyPricesResult>;

public record SyncUsDailyPricesResult(
    int Attempted,
    int Synced,
    int BarsUpserted,
    int Failed,
    DateTime? MaxLatestDate,
    string? Error);
