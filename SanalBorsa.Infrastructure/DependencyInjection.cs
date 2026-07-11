using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Quartz;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Domain.Interfaces;
using SanalBorsa.Infrastructure.Data;
using SanalBorsa.Infrastructure.ExternalServices.Bist;
using SanalBorsa.Infrastructure.ExternalServices.IsYatirim;
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

            // DailyPriceUpdateJob — every weekday at 19:00 Turkey time (UTC+3 → 16:00 UTC)
            var dailyKey = new JobKey("DailyPriceUpdateJob", "DataSync");
            q.AddJob<DailyPriceUpdateJob>(opts => opts.WithIdentity(dailyKey));
            q.AddTrigger(opts => opts
                .ForJob(dailyKey)
                .WithIdentity("DailyPriceUpdateTrigger", "DataSync")
                .WithCronSchedule("0 0 16 ? * MON-FRI", x => x.InTimeZone(TimeZoneInfo.Utc)));

            // HistoryRefreshJob — every weekday at 20:00 Turkey time (17:00 UTC)
            var refreshKey = new JobKey("HistoryRefreshJob", "DataSync");
            q.AddJob<HistoryRefreshJob>(opts => opts.WithIdentity(refreshKey));
            q.AddTrigger(opts => opts
                .ForJob(refreshKey)
                .WithIdentity("HistoryRefreshTrigger", "DataSync")
                .WithCronSchedule("0 0 17 ? * MON-FRI", x => x.InTimeZone(TimeZoneInfo.Utc)));
        });

        services.AddQuartzHostedService(opts => opts.WaitForJobsToComplete = true);

        // ── Background seeder ─────────────────────────────────────────────────
        services.AddHostedService<InitialDataSeedService>();

        return services;
    }
}
