using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Quartz;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Domain.Interfaces;
using SanalBorsa.Infrastructure.Auth;
using SanalBorsa.Infrastructure.Data;
using SanalBorsa.Infrastructure.ExternalServices.Binance;
using SanalBorsa.Infrastructure.ExternalServices.Bist;
using SanalBorsa.Infrastructure.ExternalServices.IsYatirim;
using SanalBorsa.Infrastructure.ExternalServices.Kap;
using SanalBorsa.Infrastructure.ExternalServices.YahooFinance;
using SanalBorsa.Infrastructure.Jobs;

namespace SanalBorsa.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── EF Core ────────────────────────────────────────────────────────────
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                sql => sql.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(30),
                    errorNumbersToAdd: null)));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddMemoryCache();

        // ── Binance public market data ────────────────────────────────────────
        services.AddHttpClient("Binance", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Add("User-Agent", "SanalBorsa/1.0");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });
        services.AddScoped<IBinanceMarketClient, BinanceMarketClient>();
        services.AddScoped<ICryptoMarketService, CryptoMarketService>();
        services.AddSingleton<ICryptoLiveTickerStore, CryptoLiveTickerStore>();
        // ICryptoTickerPublisher API katmanında SignalR ile override edilir; yoksa no-op.
        services.TryAddSingleton<ICryptoTickerPublisher, NullCryptoTickerPublisher>();
        services.AddHostedService<BinanceTickerStreamService>();

        // ── Firebase Admin SDK ────────────────────────────────────────────────
        // Initialization is deferred to IFirebaseAuthProvider.VerifyIdTokenAsync
        // so EF migrations and other design-time tools can start without credentials.
        services.AddScoped<IFirebaseAuthProvider, FirebaseAuthProvider>();
        services.AddSingleton<FirebaseInitializer>(sp =>
            new FirebaseInitializer(configuration));

        // ── JWT ───────────────────────────────────────────────────────────────
        services.AddScoped<IJwtService, JwtService>();

        // ── Yahoo Finance HTTP client ──────────────────────────────────────────
        services.AddHttpClient("YahooFinance", client =>
        {
            client.BaseAddress = new Uri("https://query2.finance.yahoo.com/");
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "application/json,text/plain,*/*");
            client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            client.DefaultRequestHeaders.Add("Referer", "https://finance.yahoo.com/");
            client.Timeout = TimeSpan.FromSeconds(60);
        })
        .AddStandardResilienceHandler(options =>
        {
            options.Retry.MaxRetryAttempts = 3;
            options.Retry.Delay = TimeSpan.FromSeconds(2);
        });

        services.AddScoped<IYahooFinanceService, YahooFinanceService>();

        services.AddHttpClient("IsYatirim", client =>
        {
            client.BaseAddress = new Uri("https://www.isyatirim.com.tr/");
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "application/json,text/plain,*/*");
            client.Timeout = TimeSpan.FromSeconds(120);
        });

        services.AddScoped<IIsYatirimPriceService, IsYatirimPriceService>();
        services.AddScoped<IIsYatirimCorporateActionService, IsYatirimCorporateActionService>();

        services.AddHttpClient("Kap", client =>
        {
            client.BaseAddress = new Uri("https://www.kap.org.tr/");
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "application/json,text/plain,*/*");
            client.DefaultRequestHeaders.Add("Referer", "https://www.kap.org.tr/tr/bildirim-sorgu");
            client.Timeout = TimeSpan.FromSeconds(120);
        });

        services.AddScoped<IKapCorporateActionService, KapCorporateActionService>();

        services.AddHttpClient("BistSymbols", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        });

        services.AddScoped<IBistSymbolProvider, KapBistSymbolProvider>();

        // ── Quartz.NET ────────────────────────────────────────────────────────
        services.AddQuartz(q =>
        {
            var dailyKey = new JobKey("DailyPriceUpdateJob", "DataSync");
            q.AddJob<DailyPriceUpdateJob>(opts => opts.WithIdentity(dailyKey));
            q.AddTrigger(opts => opts
                .ForJob(dailyKey)
                .WithIdentity("DailyPriceUpdateTrigger", "DataSync")
                .WithCronSchedule("0 0 16 ? * MON-FRI", x => x.InTimeZone(TimeZoneInfo.Utc)));

            var refreshKey = new JobKey("HistoryRefreshJob", "DataSync");
            q.AddJob<HistoryRefreshJob>(opts => opts.WithIdentity(refreshKey));
            q.AddTrigger(opts => opts
                .ForJob(refreshKey)
                .WithIdentity("HistoryRefreshTrigger", "DataSync")
                .WithCronSchedule("0 0 17 ? * MON-FRI", x => x.InTimeZone(TimeZoneInfo.Utc)));

            // 23:00 Turkey (UTC+3) = 20:00 UTC — İş Yatırım corporate actions
            var corpKey = new JobKey("CorporateActionSyncJob", "DataSync");
            q.AddJob<CorporateActionSyncJob>(opts => opts.WithIdentity(corpKey));
            q.AddTrigger(opts => opts
                .ForJob(corpKey)
                .WithIdentity("CorporateActionSyncTrigger", "DataSync")
                .WithCronSchedule("0 0 20 * * ?", x => x.InTimeZone(TimeZoneInfo.Utc)));

            // 23:00 Turkey — TradingView raw price incremental fill
            var tvKey = new JobKey("TradingViewPriceSyncJob", "DataSync");
            q.AddJob<TradingViewPriceSyncJob>(opts => opts.WithIdentity(tvKey));
            q.AddTrigger(opts => opts
                .ForJob(tvKey)
                .WithIdentity("TradingViewPriceSyncTrigger", "DataSync")
                .WithCronSchedule("0 0 20 * * ?", x => x.InTimeZone(TimeZoneInfo.Utc)));

            // 01:30 UTC — Binance USDT günlük kline incremental
            var cryptoHistKey = new JobKey("CryptoHistorySyncJob", "DataSync");
            q.AddJob<CryptoHistorySyncJob>(opts => opts.WithIdentity(cryptoHistKey));
            q.AddTrigger(opts => opts
                .ForJob(cryptoHistKey)
                .WithIdentity("CryptoHistorySyncTrigger", "DataSync")
                .WithCronSchedule("0 30 1 * * ?", x => x.InTimeZone(TimeZoneInfo.Utc)));

            // 23:05 Turkey — top gainers (week / month / year) after price sync
            var topKey = new JobKey("TopGainersJob", "DataSync");
            q.AddJob<TopGainersJob>(opts => opts.WithIdentity(topKey));
            q.AddTrigger(opts => opts
                .ForJob(topKey)
                .WithIdentity("TopGainersTrigger", "DataSync")
                .WithCronSchedule("0 5 20 * * ?", x => x.InTimeZone(TimeZoneInfo.Utc)));
        });

        services.AddQuartzHostedService(opts => opts.WaitForJobsToComplete = true);

        // ── Background seeder ─────────────────────────────────────────────────
        services.AddHostedService<InitialDataSeedService>();

        return services;
    }
}
