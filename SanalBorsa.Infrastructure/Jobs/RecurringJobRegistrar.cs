using Hangfire;
using SanalBorsa.Application.Common;
using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Infrastructure.Jobs;

/// <summary>
/// Gece işlerinin cron tanımlarını Hangfire'a kaydeder. SQL Server storage kalıcı olduğu için
/// process yeniden başlasa/uykuya dalsa bile "sırası gelmiş ama tetiklenememiş" job, ilk ayağa
/// kalkışta Hangfire tarafından bir kez telafi (catch-up) çalıştırılır — eski Quartz RAMJobStore'da
/// (bellekte, kalıcı değildi) bu kayboluyordu.
/// </summary>
public static class RecurringJobRegistrar
{
    public static void RegisterAll(IRecurringJobManager jobs)
    {
        var turkeyTz = ResolveTurkeyTimeZone();

        // 18:30 TR — metadata + BIST ham günlük fiyat (TradingView WS) + AdjustedClose
        jobs.AddOrUpdate<TradingViewPriceSyncJob>(
            "tradingview-price-sync",
            job => job.RunAsync(CancellationToken.None),
            "30 18 * * *",
            new RecurringJobOptions { TimeZone = turkeyTz });

        // 18:35 TR — KAP corporate actions (incremental)
        jobs.AddOrUpdate<CorporateActionSyncJob>(
            "corporate-action-sync",
            job => job.RunAsync(CancellationToken.None),
            "35 18 * * *",
            new RecurringJobOptions { TimeZone = turkeyTz });

        // 18:45 TR — BIST intraday sparkline (18:30 fiyat senkronundan sonra)
        jobs.AddOrUpdate<IntradaySparklineSyncJob>(
            "intraday-sparkline-sync-bist",
            job => job.RunAsync(MarketType.Bist, CancellationToken.None),
            "45 18 * * *",
            new RecurringJobOptions { TimeZone = turkeyTz });

        // 16:05 ET — ABD intraday sparkline (NYSE kapanışından hemen sonra, DST otomatik)
        jobs.AddOrUpdate<IntradaySparklineSyncJob>(
            "intraday-sparkline-sync-us",
            job => job.RunAsync(MarketType.UsStocks, CancellationToken.None),
            "5 16 * * 1-5",
            new RecurringJobOptions { TimeZone = NyseTradingHours.ResolveEasternTimeZone() });

        // 23:00 TR — dönem şampiyonları (top gainers)
        jobs.AddOrUpdate<TopGainersJob>(
            "top-gainers-compute",
            job => job.RunAsync(CancellationToken.None),
            "0 23 * * *",
            new RecurringJobOptions { TimeZone = turkeyTz });

        // 02:00 TR — parite sync + zaman makinesi lider tablosu
        jobs.AddOrUpdate<TimeMachineLeadersJob>(
            "time-machine-leaders-compute",
            job => job.RunAsync(CancellationToken.None),
            "0 2 * * *",
            new RecurringJobOptions { TimeZone = turkeyTz });

        // 04:30 TR (01:30 UTC) — Binance USDT günlük kline
        jobs.AddOrUpdate<CryptoHistorySyncJob>(
            "crypto-history-sync",
            job => job.RunAsync(CancellationToken.None),
            "30 1 * * *",
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
    }

    private static TimeZoneInfo ResolveTurkeyTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows() ? "Turkey Standard Time" : "Europe/Istanbul");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        }
    }
}
