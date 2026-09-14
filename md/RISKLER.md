# Riskler — canlı takip listesi

> Backend + frontend taraması sırasında bulunan güvenlik/veri/mimari riskleri. Kaynak proje özetleri:
> `SanalBorsa/md/OZET.md`, `sanal-borsa-ui/md/OZET.md`.
> Son güncelleme: 2026-09-14 (secret dışındaki tüm öneriler uygulandı).

## 🔴 Kritik — açık

### 1. `appsettings.*.json` git'e commit'li, repo public
`github.com/ahmetgkdmr/SanalBorsaV2` **public** ve şu değerler açık metin commit'li:

| Değer | Dosya |
|---|---|
| DB `sa` şifresi (`Sanal!Borsa2026`) + sunucu IP'si | Development + Production |
| JWT signing secret (dev ve prod'da **aynı**) | Development + Production |
| Hangfire dashboard şifresi | Production |

Bu secret'lar git **geçmişinde** duruyor — dosyayı şimdi düzeltmek yetmez, geçmişten okunabilir.

**Yapılması gereken (sırayla):**
1. **Önce rotate et:** DB şifresi, JWT secret, Hangfire şifresi. (JWT secret değişince mevcut tüm token'lar geçersiz olur — kullanıcılar yeniden giriş yapar, beklenen davranış.)
2. Gerçek değerleri sunucuda ortam değişkenine taşı: `ConnectionStrings__DefaultConnection`, `Jwt__Secret`, `Hangfire__DashboardPassword`, `Admin__ApiKey`. ASP.NET Core ortam değişkenlerini appsettings'in üzerine yazar, kod değişikliği gerekmez.
3. `git rm --cached SanalBorsa/appsettings.Development.json SanalBorsa/appsettings.Production.json` + `.gitignore`'a ekle. **Dikkat:** CI (`deploy.yml`) publish çıktısını SCP ile atıyor; dosya repodan çıkınca sunucuda ortam değişkenleri hazır olmalı, yoksa deploy sonrası uygulama ayağa kalkmaz.
4. Geçmişi temizlemek istersen `git filter-repo` / BFG gerekir (yeni commit geçmişi silmez).

> Bu madde bilinçli olarak koda dokunularak "düzeltilmedi": secret'ları çalışma ağacından silmek, rotate edilmedikçe yanlış bir güvenlik hissi verir ve deploy'u kırar. Rotate + ortam değişkeni adımı operasyonel, sende.

### 2. Dev ortamı gerçek veritabanına bağlı
`appsettings.Development.json` → `167.233.147.18` (uzak sunucu, `sa` kullanıcısı). Ayrı bir dev/staging DB yok; yerel geliştirmedeki her hata (yanlış sync parametresi, yanlış DELETE) gerçek veriye işler.
**Yapılması gereken:** Yerel bir SQL Server (Docker) + `dotnet ef database update`, ya da en azından ayrı bir staging DB.

## 🟠 Orta — açık

_Bu başlıkta açık madde kalmadı._

## 🟡 Düşük — açık

### 3. Token'lar `localStorage`'da düz JSON
XSS senaryosunda access + refresh birlikte çalınabilir. SPA'lar için yaygın trade-off; bilinçli kabul ediliyorsa sorun değil.

---

## ✅ Çözülenler

### 2026-09-14 (öneri paketi)

| Ne | Nasıl |
|---|---|
| **Refresh token iptal edilemiyordu** — sızan token 30 gün geçerli kalıyor, çıkış onu iptal etmiyordu | `RefreshTokens` tablosu (jti bazlı, token'ın kendisi saklanmıyor). Yenilemede **rotasyon**: yeni çift üretilip eski iptal ediliyor, aynı token ikinci kez kullanılırsa reddedilip loglanıyor. `POST /api/auth/logout` tüm aktif token'ları iptal ediyor. Süresi geçenler her gece 04:00'te siliniyor |
| **Refresh akışı zaten bozukmuş** (bu iş sırasında ortaya çıktı) | `JwtSecurityTokenHandler` varsayılan olarak `sub` claim'ini `ClaimTypes.NameIdentifier`'a eşliyor; `FindFirst("sub")` null dönüyordu, yani yenileme sessizce başarısız olup kullanıcıyı çıkışa düşürüyordu. 24 saatlik token ömrü bunu maskelemiş. `MapInboundClaims = false` ile düzeltildi |
| **`InvalidOperationException` → 400 toptan eşlemesi** iç hata metinlerini sızdırıyordu | `BusinessRuleException` eklendi; 40 kullanıma-dönük fırlatma ona çevrildi. Framework kaynaklı olanlar artık 500 + genel metin |
| **Uygulama katmanında hiç validator yoktu** | 11 validator (6 alım/satım + 5 kimlik). Biçim doğrulaması validator'da, durum gerektirenler (bakiye, seans, benzersizlik) handler'da. Kullanıcı adı deseni `ValidationRules`'ta tekilleştirildi; tek emir üst sınırı eklendi |
| **Liderlik tablosu uydurma veri gösteriyordu** | `GET /api/leaderboard` + `GET /api/leaderboard/{username}/trades`. Portföy değeri sunucuda canlı fiyatlarla hesaplanıyor (USD varlıklar kurla çevriliyor), 60 sn önbellek, N+1 yok. Gizlilik ayarına saygılı. `leaderboard.mock.ts` silindi |
| **Hiç test yoktu** | `SanalBorsa.Tests` (xUnit + NSubstitute + FluentAssertions) — **69 test**: zaman makinesi para matematiği, alım/satım iş kuralları, liderlik para birimi çevrimi, doğrulayıcılar, entity→DTO dönüşümleri, seans saatleri |
| Seans saati testlerini imkânsız kılan statik zaman bağımlılığı | `IClock` soyutlaması; `EnsureOpen(utcNow)` parametre alıyor. Testler artık günün saatinden bağımsız |
| Angular hâlâ zone.js tabanlı değişiklik algılamadaydı | `provideZonelessChangeDetection()`. Geçiş öncesi tüm `setTimeout`/`setInterval` kullanımları tarandı; `market.page.ts`'teki `searchFocused` düz alan olduğu için signal'a çevrildi (aksi hâlde arama önerileri güncellenmezdi) |
| God component'ler (2120 / 1615 / 1249 satır) | Inline stiller 9 bileşenden ayrı `.css` dosyalarına çıkarıldı (~3.170 satır). Saf canvas yardımcıları (`roundRectPath`, `wrapTextLines`, `downloadBlob`) `core/utils/canvas.util.ts`'e taşındı. En büyük dosya 2120 → 1353 satır |
| Backend README yoktu | `README.md`: kurulum, katman yapısı, yapılandırma tablosu, kimlik akışı, operasyonel uçlar, migration, test, veri kaynakları, dağıtım |

### 2026-09-07 (tam denetim)

| Ne | Nasıl |
|---|---|
| **`/api/admin/wipe-users`** — DB + Firebase'deki TÜM kullanıcıları silen endpoint, tek koruması git'e commit'li bir header secret'ı | Controller, `WipeAllUsersCommand` ve artık sahipsiz kalan `IFirebaseAuthProvider.DeleteAllUsersAsync` tamamen kaldırıldı (kodun kendi yorumu "iş bitince silinebilir" diyordu) |
| **27 operasyonel endpoint kimlik doğrulamasızdı** (sembol rename, portföylere kurumsal işlem uygulama, saatler süren sync'ler) | `AdminApiKeyAttribute` eklendi: `X-Admin-Key` ↔ `Admin:ApiKey`, sabit zamanlı karşılaştırma. Anahtar tanımsızsa **üretimde reddeder**, geliştirmede serbest bırakır (fail-closed). Hangfire job'ları etkilenmez — onlar MediatR'ı doğrudan çağırıyor |
| **CORS prod'da `AllowAnyOrigin: true` + `AllowCredentials()`** — pratikte her siteden kimlik doğrulamalı istek | `false` yapıldı; `AllowedOrigins`'e eksik olan `sanalportfoy.com` / `www.sanalportfoy.com` eklendi, localhost prod listesinden çıkarıldı. Ayrıca Program.cs'te bayrak üretimde kod düzeyinde yok sayılıyor — tekrar açılsa bile açık geri gelmez |
| **Auth uçlarında rate limit yoktu** (brute-force / kullanıcı adı keşfi) | .NET yerleşik rate limiter: IP başına dakikada 10 istek, aşınca 429. Doğrulandı: 10× 401 → 3× 429 |
| **`AutoMapper 12.0.1` — yüksek önem dereceli açık (GHSA-rvv3-g6hj-g44x)** | Bağımlılık tamamen kaldırıldı. Sadece 3 basit dönüşüm için kullanılıyordu ve zaten `ConstructUsing` ile elle yazılmıştı → `EntityMappingExtensions` (açık uzantı metotları). `dotnet list package --vulnerable` artık temiz |
| **`authInterceptor` token'ı hedefe bakmadan ekliyordu** | Sadece `environment.apiUrl` / `hubUrl` ve göreli yollara ekleniyor |
| **`AuthService.loginDemo`** — backend'i atlayıp istemcide sahte oturum + 1.000.000 ₺ üreten ölü metot | Kaldırıldı |
| **Liderlik tablosu uydurma veriyi gerçekmiş gibi sunuyordu** | Sayfaya belirgin "örnek veri" uyarısı eklendi |
| **`localStorage` sarmalanmamıştı** — gizli sekmede/depolama kapalıyken giriş akışı patlıyordu | `saveSession`/`loadSession`/`clearSession` try/catch'e alındı, oturum bellekte devam ediyor |
| **`AuthService.currentUser`/`isLoggedIn` sahte signal'dı** (ok fonksiyonu + tip cast) | Gerçek `computed()` signal'a çevrildi — memoization + doğru reaktif grafik |
| **Ölü kod** | Backend: `YahooQuoteSummaryResponse.cs` (5 tip, hiç referans yok). Frontend: `IndexTicker`, `MINIMUM_WAGE_BY_YEAR` (@deprecated), `STARTING_CASH`, `UserSession` (@deprecated), `tickLiveStocks` (rastgele sahte fiyat üreten kalıntı) |

### 2026-08-02

| Ne | Nasıl |
|---|---|
| Gece job'ları haftalarca sessizce atlanıyordu | Quartz (RAMJobStore) → Hangfire (SQL Server storage, kalıcı + catch-up). Detay: `md/JOBLAR.md` |
| LRSHO'da bozuk TV bar'ı top-gainers'ı manipüle etmişti | Manuel re-sync |
| Fiyat sync'te aşırı bar'lara karşı koruma yoktu | %20 eşikli anomali koruması + 6 saat sonra otomatik tekrar kontrol (`IPriceAnomalyScheduler` / `PriceAnomalyRecheckJob`) |
| Anomali korumasında "domino" bug'ı | Karşılaştırma bazı gerçek gözlenen kapanışı takip edecek şekilde düzeltildi |
| Token süresi dolunca portföyde sahte "-1.000.000 ₺ zarar" | Token ömrü 24 saat + gerçek 401→refresh→retry akışı |
| Alım/satım'da concurrency koruması yoktu | 7 trade handler'ının tamamı `ConcurrencySafe.RunAsync` ile optimistic concurrency + retry kullanıyor (2026-09-07'de doğrulandı) |
| Hangfire dashboard açıktı | Basic Auth + sabit zamanlı karşılaştırma (`HangfireDashboardAuthFilter`) |
