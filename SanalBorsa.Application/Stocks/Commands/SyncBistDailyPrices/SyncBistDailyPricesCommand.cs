using MediatR;

namespace SanalBorsa.Application.Stocks.Commands.SyncBistDailyPrices;

/// <summary>
/// BIST ham günlük fiyatları LatestDataDate → bugün aralığında çeker ve upsert eder.
/// Kaynak: TradingView WebSocket (<c>adjustment=none</c>).
/// <paramref name="LookbackDays"/> verilirse LatestDataDate yerine bugünden geriye o kadar gün çekilir.
/// </summary>
public record SyncBistDailyPricesCommand(
    bool Full = false,
    string? Symbol = null,
    int? LookbackDays = null)
    : IRequest<SyncBistDailyPricesResult>;

public record SyncBistDailyPricesResult(
    int Attempted,
    int Synced,
    int BarsUpserted,
    int Failed,
    DateTime? MaxLatestDate,
    string? Error);
