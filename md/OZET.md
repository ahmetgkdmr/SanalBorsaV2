# SanalBorsa — Proje Özeti

> Yeni özellik / bugfix / job değişikliği öncesi bu dosyayı oku.  
> Kaynak: `SanalBorsa/` (.NET 8) + `sanal-borsa-ui/` (Angular).

---

## SanalBorsa nedir?

SanalBorsa, **Borsa İstanbul (BIST) hisseleri** ve **kripto paraları** takip edip, gerçek parayla değil **sanal portföyle** işlem yapmanı sağlayan bir web uygulamasıdır.

Kısaca: canlıya yakın piyasa ekranı + “keşke o gün alsaydım” simülasyonu + sanal al/sat.

İki ana parça:

| Parça | Teknoloji | Rol |
|--------|-----------|-----|
| Backend | .NET 8 | Veriyi çeker, kaydeder, hesaplar, API sunar |
| Frontend | Angular | Ekranı gösterir, kullanıcıyla konuşur |

---

## Kullanıcı ne yapabilir?

1. **Piyasa ekranı** — Hisse/kripto kartları, fiyat, değişim, hacim, küçük grafik  
2. **BIST ↔ Kripto geçişi** — Aynı arayüzde iki piyasa  
3. **Dönem şampiyonları** — Son 1 hafta / 1 ay / 1 yıl / 5 yıl / 10 yılda en çok kazanan (taçlı kart)  
4. **Zaman Makinesi** — “X tarihinde Y TL koysaydım bugün ne olurdu?”  
5. **Sanal portföy** — Giriş yapıp sanal al/sat  
6. **Liderler** — Zaman makinesi sıralamaları  

---

## Veriler nereden geliyor?

| Ne | Nereden | Ne için |
|----|---------|---------|
| BIST günlük fiyat (**ham** `Close`) | TradingView WebSocket (`adjustment=none`) | Ekranda görünen kapanış, al/sat |
| BIST **düzeltilmiş kapanış** (`AdjustedClose`) | TradingView (`adjustment=dividends`) | Uzun dönem getiri hesabı |
| Temettü / bedelli / bedelsiz | KAP (gece job); full bootstrap İş Yatırım | Zaman makinesi simülasyonu |
| Kripto fiyat (canlı) | Binance WebSocket + SignalR | Anlık ticker |
| Kripto günlük geçmiş | Binance kline | Grafik, getiri, zaman makinesi |
| Dolar / Euro / gram altın | Parite sync (gece) | Karşılaştırma |

**Ham fiyat** = o günün gerçek kapanışı (gösterim / işlem).  
**Düzeltilmiş kapanış** = temettü/bölünmeye göre düzeltilmiş; uzun vadeli “kaç kat kazandın?” için daha doğru.

Python `sync.py` / `TvSync` **yok** — tamamen .NET.

---

## Mimari — katmanlı, temiz, .NET standartlarına uygun

Desen: **Clean Architecture + CQRS (MediatR)**.

Kod yazarken / özellik eklerken hedef: **katmanlar birbirine karışmasın**, bağımlılıklar **içeri doğru** aksın, .NET ekosisteminde alışılmış sınırlar korunsun.

### Katmanlar

| Katman | Proje | Ne iş yapar | Ne yapmaz |
|--------|--------|-------------|-----------|
| **API** | `SanalBorsa` | HTTP / SignalR, auth, request → MediatR | İş kuralı, DB, dış API çağrısı |
| **Application** | `SanalBorsa.Application` | Use-case’ler (Command/Query), DTO, arayüzler | EF Core, HttpClient, Quartz detayı |
| **Domain** | `SanalBorsa.Domain` | Entity, enum, repository **arayüzleri**, saf kurallar | Framework, HTTP, SQL |
| **Infrastructure** | `SanalBorsa.Infrastructure` | EF Core, Quartz, TradingView / Binance / KAP | UI / controller |

```
UI / dış dünya
      ↓
    API  ─── sadece MediatR’a iletir
      ↓
 Application  ─── iş kuralı, orchestration
      ↓
   Domain  ─── çekirdek model (kimseye bağımlı değil)
      ↑
 Infrastructure  ─── Domain/Application arayüzlerini uygular
```

### Yazım kuralları (zorunlu disiplin)

1. **Controller şişmesin** — Controller validate eder, command/query gönderir, sonuç döner. Fiyat sync, getiri hesabı, DB sorgusu controller’da olmaz.  
2. **İş kuralı Application’da** — Yeni özellik = yeni `Command` / `Query` + `Handler`. “Bir yerde bir service’e her şeyi tıkıştır” değil.  
3. **Domain saf kalsın** — Entity’ye `HttpClient`, `DbContext`, TradingView kodu konmaz.  
4. **Infrastructure kenarda** — Dış servis adaptörleri ve EF burada. Application, somut sınıfa değil **arayüze** (`IBistRawPriceService` vb.) bağımlı olur.  
5. **CQRS net olsun** — Okuma = Query, yazma/sync = Command. İkisini tek metotta karıştırma.  
6. **DI ile bağla** — `new HttpClient()`, gizli singleton, static “global state” ile iş çözme; kayıt `DependencyInjection.cs` üzerinden.  
7. **İsimlendirme ve klasör** — Feature’a göre paketle (`Stocks/Commands/...`, `Crypto/Queries/...`). Rastgele `Helpers` / `Utils` çöplüğü büyütme.  
8. **Tek sorumluluk** — Bir handler bir iş yapsın. Sync + şampiyon hesabı + e-posta aynı yerde olmasın.  
9. **Gizli bilgi yok** — Connection string, JWT, API key commit’lenmez; `appsettings.*.json` lokal / secret store.  
10. **Kırık kısayol yok** — “Hızlı olsun diye” Application’dan EF entity’sini controller’a sızdırma; DTO kullan.

Kısaca: **.NET’te Clean Architecture nasıl yazılırsa öyle yaz** — katman sınırını bozan “geçici çözüm” kalıcı borç olur.

---

## Job’lar — bilmeyen birine anlatır gibi

Sistem her gün kendi kendine veri tazeler. Elle her hisseyi çekmeye gerek yoktur; Hangfire recurring
job’ları sırayla çalışır (2026-08-02’den önce Quartz’tı — bkz. `md/JOBLAR.md` "Neden Quartz → Hangfire?").
Job’ların canlı durumu ve geçmiş çalıştırmaları: **`/hangfire`** (Basic Auth korumalı).

Saatler **Türkiye saati (TR)**.

### Günlük akış (tek bakışta)

```
18:30  BIST fiyatları (ham + düzeltilmiş)     → TradingViewPriceSyncJob
18:35  Temettü / bedelli / bedelsiz (KAP)     → CorporateActionSyncJob
23:00  Hafta/ay/yıl şampiyonları              → TopGainersJob
02:00  Pariteler + Zaman Makinesi liderleri   → TimeMachineLeadersJob
04:30  Kripto günlük geçmiş (Binance)         → CryptoHistorySyncJob
```

### 18:30 — `TradingViewPriceSyncJob` (ana fiyat güncellemesi)

Borsa kapandıktan sonra:

1. Hisse metadata (isim vb.) güncellenir  
2. TradingView’den **ham günlük kapanışlar** çekilir  
3. Ardından **düzeltilmiş kapanışlar** yazılır  

Ekranda gördüğün “dün kaçtan kapandı?” büyük ölçüde bu job’dan gelir.

### 18:35 — `CorporateActionSyncJob` (şirket işlemleri)

KAP’tan yeni **temettü, bedelli, bedelsiz** var mı bakar; varsa kaydeder.  
Zaman Makinesi “o gün alsaydın” derken temettüyü de hesaba katabilsin diye.

### 23:00 — `TopGainersJob` (şampiyonlar)

BIST ve kripto için:

> Son 1 hafta / 1 ay / 1 yıl / 5 yıl / 10 yılda en çok kim kazandı?

Sonucu DB’ye yazar; ana sayfadaki **taçlı kartlar** buradan beslenir.  
Fiyatlar 18:30’da gelmiş olur; akşam sıralama yenilenir.

### 02:00 — `TimeMachineLeadersJob` (zaman makinesi liderleri)

1. USD/TRY, EUR/TRY, gram altın serilerini tazeler  
2. “Şu tarihte alsaydın bugün ne olurdu?” lider tablosunu **baştan** üretir  

Bugünün fiyatı değişince geçmişteki her günün sıralaması da değişir; tablo her gece yenilenir.

### 04:30 — `CryptoHistorySyncJob` (kripto günlük geçmiş)

Binance’ten USDT çiftlerinin **günlük mum** geçmişini artımlı çeker.  
Kripto grafik, getiri ve zaman makinesi için günlük veri buradan gelir.  
(Canlı fiyat ayrı kanal: Binance WebSocket, gün boyu.)

### Uygulama açılınca — `InitialDataSeedService` (saatli değil)

Backend ayağa kalkınca:

- DB boşsa ilk veriyi doldurur  
- Fiyatlar çok geride kaldıysa bir kez catch-up dener  

Günlük asıl iş yukarıdaki Quartz job’lardadır. Kayıt yeri: `Infrastructure/DependencyInjection.cs`.

---

## Unutulmaması gereken kurallar

1. BIST fiyatı **TradingView**’den gelir; biz uydurmayız, kaydedip gösteririz.  
2. Ekranda genelde **ham kapanış**; uzun getiri hesabında **düzeltilmiş kapanış** kullanılır.  
3. Job’lar birbirinin çıktısına dayanır: önce fiyat → şirket işlemleri → şampiyon / lider hesapları.  
4. Ham fiyatı Yahoo-adjusted ile ezme.  
5. TV’de bar yoksa (illiquid / işlem sırası kapalı) boş dönüş normal olabilir; bozulmuş bar yazma.  
6. Soft-pasif (`IsActive = false`) fiyat geçmişini silmez; hard delete fiyatı da götürür (cascade).

---

## Elle tetiklenen admin API’ler

Hepsi (universe/sync ve metadata sync hariç) artık **Hangfire'a enqueue** ediyor ve yanıtta `jobId`
dönüyor — ilerlemesi `/hangfire/jobs/details/{jobId}` üzerinden izlenebilir.

- `POST /api/stocks/sync-prices` — BIST TV sync (`full`, `symbol`, `lookbackDays`)  
- `POST /api/stocks/sync-adjusted-closes` — sadece AdjustedClose  
- `POST /api/stocks/sync` — metadata (senkron, jobId yok)  
- `POST /api/stocks/bootstrap` — ilk kurulum  
- `POST /api/stocks/corporate-actions/sync` — `full` / `resume`  
- `POST /api/stocks/universe/sync` — sembol ekle / soft-pasife çek (fiyat silmez, senkron)  
- `POST /api/stocks/deactivate-inactive` — TV’den bar gelmeyenleri soft-pasife çek (+ etkilendiyse top-gainers'ı da yeniler)  
- `POST /api/stocks/top-gainers/compute`  
- `POST /api/crypto/sync-history`, `POST /api/crypto/backfill-pre-binance`  
- `POST /api/time-machine/leaders/compute`  

**Uyarı:** Bu endpoint'lerin hiçbiri `[Authorize]` değil — bkz. "Bilinen riskler / TODO".

---

## Kimlik doğrulama (Auth)

İki giriş yolu, ikisi de aynı `User` tablosuna yazar:

| Yol | Akış |
|-----|------|
| Google (Firebase) | `POST /api/auth/login` (idToken) → yeni kullanıcıysa `NeedsProfile=true` + öneri kullanıcı adı → `POST /api/auth/register` ile tamamlanır |
| Kullanıcı adı + şifre | `POST /api/auth/password/register` (kayıt) / `POST /api/auth/password/login` (giriş) — e-posta istenmez |

- Şifre: PBKDF2-SHA256, 100.000 iterasyon, salt+hash `Pbkdf2PasswordHasher`'da (`Infrastructure/Auth/`).
- Access token: JWT, `Jwt:AccessTokenMinutes` (varsayılan 60 dk). Refresh token: ayrı JWT, `secret + "_refresh"` ile imzalanır, `RefreshTokenDays` (varsayılan 30 gün); **DB'de tutulmaz, iptal/revoke mekanizması yok** — sızarsa süresi dolana kadar geçerli.
- Yeni kullanıcı: 1.000.000 ₺ (BIST) + 100.000 USD (kripto) sanal bakiye.
- `ShowTradeHistoryPublic` (`PATCH /api/auth/privacy`): işlem geçmişinin herkese açık olup olmadığını kontrol eder — **şu an bunu tüketen bir "leaderboard" backend endpoint'i yok** (bkz. Bilinen riskler).

## Portföy & işlem kuralları

- `PortfolioController` tamamı `[Authorize]`; fiyat her zaman **sunucuda** son kapanıştan/derinlikten hesaplanır — istemci fiyat göndermez.
- BIST alım-satım yalnızca **18:45–ertesi gün 10:00 (TR)** arası açık (`BistTradingHours`); seans saatlerinde `BIST_CLOSED` hatası döner. Kripto 7/24 açık.
- Kripto al/sat, Binance order book derinliğinden (`GetDepthAsync`, top-20) kademe kademe erir (`CryptoMarketService.MatchBuy/MatchSell`); derinlik yetersizse işlem reddedilir.
- Alım/satım tek `SaveChangesAsync` içinde yapılır; **optimistic concurrency / row-lock yok** — aynı kullanıcıdan art arda hızlı çift istek gelirse (çift tık, çift sekme) bakiye kontrolü aynı anda geçebilir (bkz. Bilinen riskler).

## Zaman makinesi (kısa)

1. Fiyatlar DB’de **ham** tutulur.  
2. Simülasyonda corporate action (temettü, bedelsiz, bedelli) uygulanır.  
3. Lider tablosu (`TimeMachineLeader`) her gece yeniden üretilir.  
4. BIST lider getirisi `AdjustedClose` oranıdır; listede Start/End fiyatları ham `Close` olabilir.  
5. Kategoriler: BIST top-5, Crypto top-5, Parite (USDTRY, EURTRY, GRAMALTIN).  

---

## Önemli dosyalar

- Job kayıt: `Infrastructure/DependencyInjection.cs`  
- TV client: `Infrastructure/ExternalServices/TradingView/TradingViewHistoryClient.cs`  
- BIST ham: `Infrastructure/ExternalServices/Bist/BistRawPriceService.cs`  
- Ham sync: `Application/Stocks/Commands/SyncBistDailyPrices/`  
- Adjusted sync: `Application/Stocks/Commands/SyncBistAdjustedCloses/`  
- Startup seed: `Infrastructure/Jobs/InitialDataSeedService.cs`  

---

## Bilinçli olarak tutulmayanlar

- Eski Python TV sync (`TvSync` / `sync.py`)  
- Eski Yahoo günlük fiyat job’ları (`DailyPriceUpdateJob` / `HistoryRefreshJob`)  
- Python import endpoint’leri (`PUT/POST …/price-histories`, wipe)  

## Bilinen riskler / TODO (2026-08-02 taraması)

> Güncel/canlı liste: `md/RISKLER.md` (durum işaretli, backend + frontend birlikte).

**Kritik — hemen bakılmalı:**
- `SanalBorsa/appsettings.Production.json` **git’e commit’lenmiş** ve **public GitHub reposunda** — içinde gerçek Azure SQL admin şifresi (`sanalborsa_admin`) ve prod JWT secret açık metin duruyor. Şifre ve JWT secret rotasyona alınmalı, dosya git geçmişinden temizlenmeli (BFG / filter-repo), ileride `appsettings.Production.json` sadece placeholder tutup gerçek değerler ortam değişkeni / secret store’dan gelmeli.
- `StocksController`, `CryptoController`, `TimeMachineController`, `CorporateActionsController`, `IndicesController` altındaki tüm sync/bootstrap/compute endpoint’leri **`[Authorize]` değil** — herkes `POST /api/stocks/sync-prices`, `/bootstrap`, `/corporate-actions/sync` (full=true → İş Yatırım’a 30-60 dk’lık iş), `/universe/sync` (rastgele sembol ekleme) gibi ağır/veri-bozucu işleri anonim tetikleyebilir. En az admin-key veya `[Authorize(Roles="Admin")]` eklenmeli.

**Orta:**
- Login/register/refresh endpoint’lerinde rate limiting yok (`AddRateLimiter` kullanılmıyor) — brute-force / kullanıcı adı enumeration riski.
- Refresh token DB’de tutulmuyor, revoke/logout-everywhere mekanizması yok; `secret + "_refresh"` ile anahtar türetmek yerine ayrı bir secret kullanılması daha sağlam olur.
- CORS `Cors:AllowAnyOrigin=true` (appsettings.Production.json) + `AllowCredentials()` — pratikte tüm originlere açık. Bearer token kullanıldığı için CSRF riski düşük ama origin kısıtlaması fiilen devre dışı; zaten tanımlı `AllowedOrigins` listesi kullanılabilir.
- BuyStock/SellStock/BuyCrypto/SellCrypto handler’larında concurrency token yok — art arda hızlı çift istek bakiyeyi negatife düşürebilir (düşük risk, sanal para ama düzeltilmeli).
- Leaderboard (liderlik) için backend’de hiç endpoint yok; `ShowTradeHistoryPublic` alanı şu an tüketilmiyor (frontend’de mock veriyle gösteriliyor — bkz. frontend OZET).

**Çözüldü (2026-08-02):** Gece job'ları (TopGainers, TimeMachineLeaders, CryptoHistorySync) haftalarca sessizce atlanıyordu — Quartz `RAMJobStore` bellekte tutuluyordu, process her restart'ta (deploy veya Render'ın trafiksizken uykuya dalması) o güne ait tetiklemeyi kaybediyordu, hata da vermiyordu. Hangfire'a (SQL Server storage) geçildi: job durumu artık DB'de kalıcı, process ne zaman ayağa kalkarsa vakti geçmiş job'u bir kez telafi ediyor; `/hangfire` dashboard'ından görünürlük var. Detay: `md/JOBLAR.md`. **Not:** Render'ın kendisi hâlâ trafiksizken uyuyabiliyor — bu, "process tamamen kapalıyken hiçbir şey çalışmaz" senaryosunu çözmez, sadece process her uyandığında kaçırılanı yakalar. Gece boyu sürekli ayakta kalması isteniyorsa ayrıca bir keep-alive ping (örn. cron-job.org) veya Render'ın kendi Cron Job'u gerekir.
