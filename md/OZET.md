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

Sistem her gün kendi kendine veri tazeler. Elle her hisseyi çekmeye gerek yoktur; Quartz job’ları sırayla çalışır.

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

- `POST /api/stocks/sync-prices` — BIST TV sync (`full`, `symbol`, `lookbackDays`)  
- `POST /api/stocks/sync-adjusted-closes` — sadece AdjustedClose  
- `POST /api/stocks/sync` — metadata  
- `POST /api/stocks/bootstrap` — ilk kurulum  
- `POST /api/stocks/corporate-actions/sync` — `full` / `resume`  
- `POST /api/stocks/universe/sync` — sembol ekle / soft-pasife çek (fiyat silmez)  
- `POST /api/stocks/deactivate-inactive` — TV’den bar gelmeyenleri soft-pasife çek  
- `POST /api/stocks/top-gainers/compute`  
- Zaman makinesi liderleri: `TimeMachineController` (`/api/time-machine/...`)  

---

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
