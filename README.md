# SanalBorsa — Backend (.NET 8)

Borsa İstanbul, ABD hisseleri ve kripto için **sanal portföy** platformunun API'si. Gerçek para
yok; gerçek fiyat var.

> **Yeni özellik / bugfix öncesi okunacaklar:** [`md/OZET.md`](md/OZET.md) (mimari ve iş kuralları),
> [`md/JOBLAR.md`](md/JOBLAR.md) (zamanlanmış işler), [`md/RISKLER.md`](md/RISKLER.md) (açık riskler).

---

## Hızlı başlangıç

```bash
# 1) Gerekli: .NET 8 SDK + erişilebilir bir SQL Server
dotnet restore

# 2) Ayarlar (aşağıdaki "Yapılandırma" bölümüne bak) — en azından bağlantı dizesi ve JWT secret
#    Geliştirmede appsettings.Development.json kullanılabilir.

# 3) Çalıştır — migration'lar açılışta otomatik uygulanır
dotnet run --project SanalBorsa --launch-profile https
```

| Adres | Ne |
|---|---|
| `https://localhost:7285` | API |
| `https://localhost:7285/swagger` | Swagger arayüzü (sadece Development) |
| `https://localhost:7285/hangfire` | Zamanlanmış iş panosu (Basic Auth) |

Frontend ayrı depoda (`sanal-borsa-ui`, Angular) — `http://localhost:4200`.

---

## Çözüm yapısı

Clean Architecture; bağımlılıklar hep içeri doğru akar.

```
SanalBorsa.Domain          Entity'ler, enum'lar, repository arayüzleri   (bağımlılığı yok)
SanalBorsa.Application     CQRS handler'ları, DTO'lar, iş kuralları      → Domain
SanalBorsa.Infrastructure  EF Core, repository'ler, dış servisler, job'lar → Domain, Application
SanalBorsa                 Controller'lar, middleware, SignalR hub       → Application, Infrastructure
SanalBorsa.Tests           Birim testleri                                → Application
```

**Desen:** İstek → Controller → MediatR (`IRequest`) → Handler → `IUnitOfWork` → EF Core.
Pipeline'da iki davranış var: `LoggingBehavior` ve `ValidationBehavior` (FluentValidation).

### Nereye ne yazılır

| Ne yapıyorsun | Nereye |
|---|---|
| Yeni bir uç nokta | `SanalBorsa/Controllers/` + `Application/<Alan>/Commands|Queries/` |
| Girdi doğrulaması | İlgili alanın `*Validators.cs` dosyası (biçim); durum gerektirenler handler'da |
| Yeni dış veri kaynağı | `Infrastructure/ExternalServices/` + `Application/Common/Interfaces/` |
| Zamanlanmış iş | `Infrastructure/Jobs/` + `RecurringJobRegistrar` kaydı |
| Entity/şema değişikliği | `Domain/Entities/` + `Infrastructure/Data/Configurations/` + migration |

---

## Yapılandırma

Değerler `appsettings.json` → `appsettings.{Environment}.json` → **ortam değişkenleri**
sırasıyla okunur; sonraki öncekini ezer. Üretimde gizli değerler **ortam değişkeniyle**
verilmelidir (iç içe anahtarlar için `__` ayracı).

| Anahtar | Ortam değişkeni | Açıklama |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` | SQL Server bağlantı dizesi |
| `Jwt:Secret` | `Jwt__Secret` | Token imzalama anahtarı (en az 32 karakter) |
| `Jwt:AccessTokenMinutes` | — | Erişim token ömrü (varsayılan 1440) |
| `Jwt:RefreshTokenDays` | — | Yenileme token ömrü (varsayılan 30) |
| `Admin:ApiKey` | `Admin__ApiKey` | Operasyonel uçların `X-Admin-Key` anahtarı |
| `Hangfire:DashboardUser` / `…Password` | `Hangfire__DashboardUser` / `…Password` | `/hangfire` Basic Auth |
| `Cors:AllowedOrigins` | — | İzinli frontend adresleri |
| `Firebase:ServiceAccountPath` | — | Firebase Admin SDK json yolu (Google ile giriş için) |

> ⚠️ Gizli değerleri depoya commit'leme. Mevcut durum ve yapılacaklar için
> [`md/RISKLER.md`](md/RISKLER.md) → "Kritik" bölümü.

---

## Kimlik doğrulama

İki yol var: **Firebase** (Google ile giriş) ve **kullanıcı adı + şifre** (PBKDF2).
İkisi de aynı sonucu üretir: kısa ömürlü **erişim token'ı** + uzun ömürlü **yenileme token'ı**.

Yenileme token'ları veritabanında kayıtlıdır (`RefreshTokens`) ve **iptal edilebilir**:

- `POST /api/auth/refresh` → yeni çift üretir ve **eskisini iptal eder** (rotasyon). Aynı token
  ikinci kez kullanılırsa reddedilir ve loglanır.
- `POST /api/auth/logout` → kullanıcının tüm aktif yenileme token'larını iptal eder.
- Süresi geçmiş kayıtlar her gece 04:00'te temizlenir.

Kimlik uçları IP başına **dakikada 10 istek** ile sınırlıdır (aşınca 429).

---

## Operasyonel uçlar

Senkronizasyon, bootstrap, audit ve yeniden adlandırma uçları kullanıcı arayüzünden **hiç**
çağrılmaz; saatler sürebilen, dış servisleri yoran ve veriyi değiştiren bakım işleridir.
Hepsi `[AdminApiKey]` ile korunur:

```bash
curl -X POST https://.../api/stocks/sync-prices \
     -H "X-Admin-Key: $ADMIN_API_KEY"
```

`Admin:ApiKey` **tanımlı değilse**: üretimde 503 (fail-closed), geliştirmede serbest.
Hangfire'ın zamanlanmış işleri bu filtreden etkilenmez — onlar HTTP değil, doğrudan MediatR
üzerinden çalışır.

---

## Veritabanı

Migration'lar uygulama açılışında otomatik uygulanır (`Program.cs`, komut zaman aşımı 15 dk —
milyonlarca satırlık fiyat tablosunda indeks kuran migration'lar uzun sürebiliyor).

```bash
# Yeni migration
dotnet ef migrations add <Ad> --project SanalBorsa.Infrastructure --startup-project SanalBorsa

# Elle uygulama
dotnet ef database update --project SanalBorsa.Infrastructure --startup-project SanalBorsa
```

---

## Test

```bash
dotnet test SanalBorsa.Tests/SanalBorsa.Tests.csproj
```

Kapsam, hatanın en pahalı olduğu yerlere odaklı: zaman makinesi para matematiği, alım/satım
iş kuralları (ağırlıklı ortalama maliyet, bakiye, seans saati), liderlik tablosu para birimi
çevrimi, doğrulayıcılar ve entity→DTO dönüşümleri.

Zamana bağlı davranış `IClock` üzerinden test edilir — testler günün saatinden bağımsızdır.

---

## Veri kaynakları

| Kaynak | Ne için |
|---|---|
| TradingView | BIST + ABD günlük fiyat (ham ve düzeltilmiş), FX canlı akış |
| Binance | Kripto canlı fiyat (WebSocket) + geçmiş |
| KAP / İş Yatırım | BIST kurumsal işlemler (bedelli, bedelsiz, temettü) |
| Yahoo Finance | ABD sembollerinin borsasını çözmek, kurumsal işlemler |
| TCMB | Tarihsel döviz kurları |

Fiyat senkronizasyonunda **%20 eşikli anomali koruması** vardır; şüpheli bar 6 saat sonra
yeniden kontrol edilir (`PriceAnomalyRecheckJob`).

---

## Dağıtım

`main` dalına push → GitHub Actions (`.github/workflows/deploy.yml`) publish edip Hetzner
sunucusuna SCP ile atar ve `sanalportfoy-api` servisini yeniden başlatır.
Zamanlanmış işleri **yalnızca Production** sahiplenir (`Program.cs`) — geliştirme ortamı aynı
veritabanına bağlanabildiği için kayıtların birbirini ezmemesi adına.
