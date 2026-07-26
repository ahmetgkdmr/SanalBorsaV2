# SanalBorsa Backend Özeti

> Yeni özellik / bugfix öncesi bu dosyayı oku. Kaynak: `SanalBorsa/` (.NET 8).

## Mimari

| Katman | Proje | Rol |
|--------|--------|-----|
| API | `SanalBorsa` | Controllers, auth, SignalR |
| Application | `SanalBorsa.Application` | MediatR commands/queries, DTO |
| Domain | `SanalBorsa.Domain` | Entity + repository arayüzleri |
| Infrastructure | `SanalBorsa.Infrastructure` | EF Core, Quartz, dış servisler |

Desen: **Clean Architecture + CQRS (MediatR)**.

## Veri kaynakları (önemli)

| Veri | Kaynak | Not |
|------|--------|-----|
| BIST günlük **ham** OHLCV | TradingView WebSocket (`adjustment=none`) → `Close` | Zaman makinesi ham + corp-action |
| BIST **AdjustedClose** | TradingView (`adjustment=dividends`) | Aynı satırda UPDATE; Close dokunulmaz |
| BIST metadata | Yahoo / KAP seed | `SyncStocksCommand` |
| Corporate actions | Gece KAP; full bootstrap İş Yatırım | |
| Kripto günlük | Binance kline | Canlı: Binance miniTicker + SignalR |
| Endeks / FX / gram altın | Yahoo (+ türev) | Parite job’u gece |

Python `sync.py` / `TvSync` **yok** — tamamen .NET.

## Quartz job’lar (Türkiye saati)

| Saat (TR) | Job | Ne yapar |
|-----------|-----|----------|
| **19:00** | `TradingViewPriceSyncJob` | Metadata + BIST ham Close + AdjustedClose (TV) |
| **23:00** | `CorporateActionSyncJob` | KAP incremental bedelli/bedelsiz/temettü |
| **23:05** | `TopGainersJob` | BIST + Crypto dönem şampiyonları |
| **04:30** | `CryptoHistorySyncJob` | Binance USDT günlük kline |
| **05:30** | `TimeMachineLeadersJob` | Parite sync + “o gün alsaydın” lider tablosu (BIST: corp-action’lı getiri) |

Startup: `InitialDataSeedService` — boş/stale DB catch-up; günlük loop yok.

## Elle tetiklenen admin API’ler

- `POST /api/stocks/sync-prices` — BIST TV sync (`full`, `symbol`)
- `POST /api/stocks/sync` — metadata
- `POST /api/stocks/bootstrap` — ilk kurulum
- `POST /api/stocks/corporate-actions/sync` — `full` / `resume`
- `POST /api/stocks/universe/sync` — sembol ekle/çıkar
- `POST /api/stocks/top-gainers/compute`
- Time machine leaders: `TimeMachineController` (`/api/time-machine/...`)

## Zaman makinesi kuralları

1. Fiyatlar DB’de **ham** tutulur.
2. Getiri hesaplanırken corporate action (temettü, bedelsiz, bedelli) uygulanır.
3. Lider tablosu (`TimeMachineLeader`) her gece yeniden üretilir — “bugün” kaydığı için sıralama değişir.
4. BIST lider getirisi `AdjustedClose` (TV) oranıdır; listedeki Start/End fiyatları ham `Close`.
5. Kategoriler: BIST top-5, Crypto top-5, Parite (USDTRY, EURTRY, GRAMALTIN).

## Önemli dosyalar

- TV client: `Infrastructure/ExternalServices/TradingView/TradingViewHistoryClient.cs`
- BIST ham: `Infrastructure/ExternalServices/Bist/BistRawPriceService.cs`
- Sync: `Application/Stocks/Commands/SyncBistDailyPrices/`
- Job kayıt: `Infrastructure/DependencyInjection.cs`
- Seed: `Infrastructure/Jobs/InitialDataSeedService.cs`

## Bilinçli olarak tutulmayanlar

- Eski Python TV sync config (`TvSync`)
- `DailyPriceUpdateJob` / `HistoryRefreshJob` (Yahoo fiyat yolu — kaldırıldı)
- Python import endpoint’leri (`PUT/POST …/price-histories`, wipe/old)

## Geliştirme notları

- Ham fiyat değiştirirken Yahoo-adjusted kullanma.
- Illiquid hisseler (ISATR, ISKUR, UMPAS) her gün işlem görmeyebilir — TV’de bar yoksa boş dönüş normal.
- Connection string / JWT secret’ları commit’leme; `appsettings.*.json` lokal.
