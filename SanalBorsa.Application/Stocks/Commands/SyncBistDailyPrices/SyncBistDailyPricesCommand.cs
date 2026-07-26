using MediatR;

namespace SanalBorsa.Application.Stocks.Commands.SyncBistDailyPrices;

/// <summary>
/// BIST ham günlük fiyatları LatestDataDate → bugün aralığında çeker ve upsert eder.
/// Kaynak: TradingView WebSocket (<c>adjustment=none</c>).
/// </summary>
public record SyncBistDailyPricesCommand(bool Full = false, string? Symbol = null)
    : IRequest<SyncBistDailyPricesResult>;

public record SyncBistDailyPricesResult(
    int Attempted,
    int Synced,
    int BarsUpserted,
    int Failed,
    DateTime? MaxLatestDate,
    string? Error);
