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

        // 18:30 TR — metadata + BIST ham günlük fiyat (TradingView WS)
        jobs.AddOrUpdate<TradingViewPriceSyncJob>(
            "tradingview-price-sync",
            job => job.RunAsync(CancellationToken.None),
            "30 18 * * *",
            new RecurringJobOptions { TimeZone = turkeyTz });

        // 18:35 TR — KAP corporate actions (incremental) + sadece etkilenen hisseler için AdjustedClose yenilemesi
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
        var easternTz = NyseTradingHours.ResolveEasternTimeZone();
        jobs.AddOrUpdate<IntradaySparklineSyncJob>(
            "intraday-sparkline-sync-us",
            job => job.RunAsync(MarketType.UsStocks, CancellationToken.None),
            "5 16 * * 1-5",
            new RecurringJobOptions { TimeZone = easternTz });

        // 16:10 ET — ABD ham günlük fiyat senkronu (BIST'teki tradingview-price-sync'in ABD karşılığı)
        jobs.AddOrUpdate<UsPriceSyncJob>(
            "us-price-sync",
            job => job.RunAsync(CancellationToken.None),
            "10 16 * * 1-5",
            new RecurringJobOptions { TimeZone = easternTz });

        // 16:20 ET — ABD kurumsal olaylar (Yahoo) + sadece etkilenen hisseler için AdjustedClose yenilemesi
        jobs.AddOrUpdate<UsCorporateActionSyncJob>(
            "us-corporate-action-sync",
            job => job.RunAsync(CancellationToken.None),
            "20 16 * * 1-5",
            new RecurringJobOptions { TimeZone = easternTz });

        // 23:00 TR — dönem şampiyonları (top gainers) — BIST ve Crypto
        jobs.AddOrUpdate<TopGainersJob>(
            "top-gainers-compute",
            job => job.RunAsync(new[] { MarketType.Bist, MarketType.Crypto }, CancellationToken.None),
            "0 23 * * *",
            new RecurringJobOptions { TimeZone = turkeyTz });

        // 16:30 ET — dönem şampiyonları (top gainers) — ABD (ayrı saat, bkz. TopGainersJob üstündeki not)
        jobs.AddOrUpdate<TopGainersJob>(
            "top-gainers-compute-us",
            job => job.RunAsync(new[] { MarketType.UsStocks }, CancellationToken.None),
            "30 16 * * 1-5",
            new RecurringJobOptions { TimeZone = easternTz });

        // 02:00 TR — parite sync + zaman makinesi lider tablosu (BIST, Crypto, ABD, Parite — hepsi bu saatte
        // ABD'nin gece yarısı öncesi tamamlanan senkronundan sonra gelir, mevsimden bağımsız güvenli)
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
