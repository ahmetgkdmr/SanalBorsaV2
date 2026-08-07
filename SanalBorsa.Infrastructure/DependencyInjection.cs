using Hangfire;
using Hangfire.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Domain.Interfaces;
using SanalBorsa.Infrastructure.Auth;
using SanalBorsa.Infrastructure.Data;
using SanalBorsa.Infrastructure.ExternalServices.Binance;
using SanalBorsa.Infrastructure.ExternalServices.Bist;
using SanalBorsa.Infrastructure.ExternalServices.Coinbase;
using SanalBorsa.Infrastructure.ExternalServices.IsYatirim;
using SanalBorsa.Infrastructure.ExternalServices.Zorinaq;
using SanalBorsa.Infrastructure.ExternalServices.Kap;
using SanalBorsa.Infrastructure.ExternalServices.Tcmb;
using SanalBorsa.Infrastructure.ExternalServices.TradingView;
using SanalBorsa.Infrastructure.ExternalServices.YahooFinance;
using SanalBorsa.Infrastructure.Jobs;

namespace SanalBorsa.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // ── EF Core ────────────────────────────────────────────────────────────
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                sql =>
                {
                    sql.CommandTimeout(3600); // leaders / bulk fiyat sorguları + büyük tablo ALTER COLUMN migration'ları
                    sql.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(30),
                        errorNumbersToAdd: null);
                }));

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
        // Gerçek forex/emtia kuru (USD/TRY, EUR/TRY, gram altın) — Binance'in USDT/TRY paritesinden
        // farklı olarak TradingView'ın interbank kaynağından, aynı ticker store/publisher üzerinden.
        services.AddHostedService<SanalBorsa.Infrastructure.ExternalServices.TradingView.TvFxTickerStreamService>();

        // ── Firebase Admin SDK ────────────────────────────────────────────────
        // Initialization is deferred to IFirebaseAuthProvider.VerifyIdTokenAsync
        // so EF migrations and other design-time tools can start without credentials.
        services.AddScoped<IFirebaseAuthProvider, FirebaseAuthProvider>();
        services.AddSingleton<FirebaseInitializer>();


        // ── JWT + password hashing ────────────────────────────────────────────
        services.AddScoped<IJwtService, JwtService>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();

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

        // Retry'siz Yahoo — crypto backfill probe (404 yağmurunda yavaşlamasın)
        services.AddHttpClient("YahooFinanceProbe", client =>
        {
            client.BaseAddress = new Uri("https://query2.finance.yahoo.com/");
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "application/json,text/plain,*/*");
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        // ── Coinbase Exchange (crypto USD history backfill) ───────────────────
        services.AddHttpClient("Coinbase", client =>
        {
            client.BaseAddress = new Uri("https://api.exchange.coinbase.com/");
            client.DefaultRequestHeaders.Add("User-Agent", "SanalBorsa/1.0");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.Timeout = TimeSpan.FromSeconds(60);
        });
        services.AddScoped<ICoinbaseMarketClient, CoinbaseMarketClient>();

        // ── Zorinaq BTC archive (pre-Yahoo / pre-Binance) ─────────────────────
        services.AddHttpClient("ZorinaqArchive", client =>
        {
            client.BaseAddress = new Uri("https://price.bublina.eu.org/");
            client.DefaultRequestHeaders.Add("User-Agent", "SanalBorsa/1.0");
            client.DefaultRequestHeaders.Add("Accept", "text/plain,*/*");
            client.Timeout = TimeSpan.FromSeconds(60);
        });
        services.AddScoped<IZorinaqBitcoinArchiveClient, ZorinaqBitcoinArchiveClient>();

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

        // ── TradingView WebSocket + BIST ham fiyat (Python sync.py yerine) ───
        services.AddScoped<TradingViewHistoryClient>();
        services.AddScoped<ITradingViewHistoryService>(sp => sp.GetRequiredService<TradingViewHistoryClient>());
        services.AddScoped<IBistRawPriceService, BistRawPriceService>();
        services.AddScoped<IPriceAnomalyScheduler, HangfirePriceAnomalyScheduler>();
        services.AddScoped<SanalBorsa.Application.Common.Services.PriceAnomalyGuard>();
        services.AddScoped<IPortfolioFxRateProvider, SanalBorsa.Infrastructure.ExternalServices.Fx.PortfolioFxRateProvider>();

        services.AddHttpClient("Tcmb", client =>
        {
            client.BaseAddress = new Uri("https://www.tcmb.gov.tr/");
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "application/xml,text/xml,*/*");
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddScoped<ITcmbFxHistoryService, TcmbFxHistoryService>();

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

        // ── Hangfire (SQL Server storage — kalıcı; process restart/uyku job'u kaybetmez) ──
        // Quartz'ın RAMJobStore'u (bellekte) yerine geçti: recurring job'ların "sıradaki
        // çalışma zamanı" artık DB'de tutuluyor. Process ne zaman ayağa kalkarsa kalksın,
        // vakti geçmiş bir job varsa Hangfire onu bir kez telafi (catch-up) çalıştırır.
        // Cron kayıtları: Jobs/RecurringJobRegistrar.cs (Program.cs'te app.Build() sonrası çağrılır).
        services.AddHangfire((sp, config) => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(
                configuration.GetConnectionString("DefaultConnection"),
                new SqlServerStorageOptions
                {
                    SchemaName = "Hangfire",
                    PrepareSchemaIfNecessary = true,
                }));

        // Lokal geliştirme ortamında worker (job'ları gerçekten ÇALIŞTIRAN taraf) hiç ayağa
        // kalkmasın — dev makinesi artık production ile AYNI veritabanına bağlanıyor, ikisi de
        // worker olursa aynı senkronları eşzamanlı/çakışarak çalıştırıp yavaşlatıyorlardı (bkz.
        // proje sohbeti). Dashboard'dan job tetiklemek/izlemek hâlâ çalışır, sadece işi lokal
        // makine değil production sunucusu yürütür.
        if (environment.IsProduction())
            services.AddHangfireServer();

        // ── Startup bootstrap (günlük fiyat Hangfire 18:30'da) ─────────────────
        services.AddHostedService<InitialDataSeedService>();

        return services;
    }
}
