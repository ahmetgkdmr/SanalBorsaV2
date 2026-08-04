# Job takvimi (kısa)

Türkiye saati (UTC+3). Detay: `SanalBorsa/md/OZET.md`.

**2026-08-02'den itibaren: Hangfire (SQL Server storage), Quartz değil.** Recurring job kayıtları
`Infrastructure/Jobs/RecurringJobRegistrar.cs`'te. Canlı durum + geçmiş çalıştırmalar: `/hangfire`
(Basic Auth — kimlik bilgisi `Hangfire:DashboardUser`/`DashboardPassword`, prod'da ortam değişkeni
`Hangfire__DashboardUser` / `Hangfire__DashboardPassword` ile set edilir, appsettings'e yazılmaz).

| TR saati | Job | Hangfire recurring job id |
|---------|-----|-----|
| 18:30 | BIST metadata + TradingView ham/AdjustedClose fiyat | `tradingview-price-sync` |
| 18:35 | KAP corporate actions (incremental) | `corporate-action-sync` |
| 23:00 | Top gainers (BIST + Crypto) | `top-gainers-compute` |
| 02:00 | Parite + zaman makinesi liderleri | `time-machine-leaders-compute` |
| 04:30 | Binance kripto günlük (01:30 UTC) | `crypto-history-sync` |

Startup: eksik/stale veri catch-up (`InitialDataSeedService`).

**Neden Quartz → Hangfire?** Quartz `RAMJobStore` kullanıyordu (bellekte, kalıcı değil) — process
her restart olduğunda (deploy veya Render'ın trafiksizken uykuya dalması) o güne ait tetikleme
kaybolabiliyordu, hiçbir hata/log bırakmadan. Hangfire'ın SQL Server storage'ı job'un "sıradaki
çalışma zamanı"nı DB'de tutuyor; process ne zaman ayağa kalkarsa kalksın, vakti geçmiş bir job'u
bir kez telafi (catch-up) çalıştırıyor. Ayrıca `/hangfire` dashboard'ından hangi job'un ne zaman
çalıştığı, başarılı/başarısız olduğu görülebiliyor (otomatik retry: 3 deneme).
