# Riskler — canlı takip listesi

> Backend + frontend taraması sırasında bulunan güvenlik/veri/mimari riskleri. Kaynak proje özetleri:
> `SanalBorsa/md/OZET.md`, `sanal-borsa-ui/md/OZET.md`. Bu dosya ikisinin risk bölümlerini tek yerde toplar.
> Son güncelleme: 2026-08-02.

## 🔴 Kritik — açık

### 1. `appsettings.Production.json` git'e commit'li, repo public
Gerçek Azure SQL admin şifresi (`sanalborsa_admin`) ve prod JWT signing secret açık metin, `github.com/ahmetgkdmr/SanalBorsaV2` **public** repoda commit'li duruyor. Herkes DB'ye bağlanabilir, JWT secret ile sahte token üretebilir.
**Yapılması gereken:** DB şifresini ve JWT secret'ı rotate et; gerçek değerleri appsettings'ten çıkarıp Render ortam değişkenine taşı; git geçmişini temizlemek istersen `git filter-repo`/BFG gerekir (basit yeni commit geçmişte hâlâ görünür bırakır).

### 2. Admin/sync endpoint'leri kimlik doğrulamasız
`StocksController`, `CryptoController`, `TimeMachineController`, `CorporateActionsController`, `IndicesController` altındaki tüm sync/bootstrap/compute endpoint'lerinde `[Authorize]` yok. Herkes `sync-prices`, `bootstrap`, `corporate-actions/sync?full=true` (30-60 dk'lık İş Yatırım işi), `universe/sync` gibi ağır/veri-bozucu işleri anonim tetikleyebilir.
**Yapılması gereken:** En az basit bir admin API-key middleware'i veya `[Authorize(Roles="Admin")]`.

## 🟠 Orta — açık

### 3. Rate limiting yok
Login/register/refresh endpoint'lerinde `AddRateLimiter` kullanılmıyor — brute-force / kullanıcı adı enumeration riski.

### 4. Refresh token iptal edilemiyor
Refresh token DB'de tutulmuyor, stateless JWT (`secret + "_refresh"` ile imzalı). Sızarsa `RefreshTokenDays` (30 gün) boyunca geçerli kalır, logout bunu iptal etmez.

### 5. CORS prod'da `AllowAnyOrigin: true`
`appsettings.Production.json` → `Cors:AllowAnyOrigin=true` + `AllowCredentials()`. Pratikte origin kısıtı devre dışı. Zaten tanımlı `AllowedOrigins` listesi var, onu kullanmak daha güvenli.

### 6. Alım/satım'da concurrency koruması yok
`BuyStock`/`SellStock`/`BuyCrypto`/`SellCrypto` handler'larında optimistic concurrency token / row-lock yok. Aynı kullanıcıdan hızlı art arda iki istek (çift tık, çift sekme) aynı bakiyeyi aynı anda görüp ikisi de geçebilir. Sanal para, düşük risk ama düzeltilmeye değer.

### 7. Leaderboard tamamen sahte veri
`/leaderboard` sayfası (`leaderboard.mock.ts`) sahte kullanıcı adları + sahte işlemler gösteriyor; gerçek kullanıcı gerçek insan sanabilir. Backend'de bunu besleyecek bir endpoint hiç yok — `ShowTradeHistoryPublic` alanı ve gizlilik toggle'ı (portföy sayfasında) zaten hazır, sadece leaderboard sorgusu eksik.

### 8. Hangfire dashboard prod'da kimlik bilgisi bekliyor
`/hangfire` Basic Auth ile korunuyor ama appsettings.Production.json'a bilinçli olarak gerçek kullanıcı/şifre **yazılmadı** (sızıntı riski olmasın diye). Render'da `Hangfire__DashboardUser` / `Hangfire__DashboardPassword` ortam değişkenleri set edilmeden deploy edilirse `/hangfire` her istekte 500 verir (güvenli varsayılan, ama unutulmamalı).

### 9. Dev ortamı prod veritabanına bağlı
`appsettings.Development.json`'daki `ConnectionStrings:DefaultConnection`, prod ile **aynı** Azure SQL'i gösteriyor — ayrı bir dev/staging DB yok. Yerel geliştirme/test sırasında yapılan yanlışlıklar (yanlış bir DELETE, yanlış bir sync parametresi) doğrudan gerçek veriye işliyor. Bu oturumdaki tüm testler de bu yüzden gerçek DB üzerinde yapıldı.

## 🟡 Düşük

### 10. `AutoMapper 12.0.1` bilinen güvenlik açığı
Build sırasında `NU1903` uyarısı — yüksek önem dereceli, `GHSA-rvv3-g6hj-g44x`. Versiyon güncellemesi değerlendirilmeli.

### 11. Frontend: `authInterceptor` token'ı her isteğe URL'e bakmadan ekliyor
Şu an sadece backend'e istek atılıyor, risk yok; ileride 3. parti bir API çağrılırsa access token o adrese de sızabilir. `req.url.startsWith(environment.apiUrl)` kontrolü önerilir.

### 12. Frontend: token'lar `localStorage`'da düz JSON
XSS senaryosunda access+refresh token birlikte çalınabilir. Mimari değişikliği gerektirmiyor ama bilinmesi gereken bir trade-off.

## ✅ Bu oturumda çözülenler

| Tarih | Ne | Nasıl |
|---|---|---|
| 2026-08-02 | Gece job'ları (TopGainers, TimeMachineLeaders, CryptoHistorySync) haftalarca sessizce atlanıyordu | Quartz (RAMJobStore, kalıcı değil) → Hangfire (SQL Server storage, kalıcı + catch-up). Detay: `md/JOBLAR.md` |
| 2026-08-02 | LRSHO'da bozuk bir TV bar'ı (94,05 ₺) top-gainers'ı manipüle etmişti | Manuel re-sync ile düzeltildi |
| 2026-08-02 | Fiyat sync'te kötü/aşırı bar'lara karşı sağlamlık kontrolü yoktu | `SyncBistDailyPricesCommandHandler`'a %20 eşikli anomali koruması + 6 saat sonra otomatik tekrar kontrol eklendi (`IPriceAnomalyScheduler` / `PriceAnomalyRecheckJob`) |
| 2026-08-02 | Anomali koruması ilk halinde "domino" bug'ı vardı (bir kere sapan hisse, sonraki HER günü de yanlışlıkla anomali sayıyordu) | Karşılaştırma bazı gerçek gözlenen kapanışı takip edecek şekilde düzeltildi |
| 2026-08-02 | Access token süresi dolunca portföy sayfası sahte "-1.000.000 ₺ zarar" gösteriyordu (401 → boş state → K/Z hesaba 0 giriyordu) | Token ömrü 60 dk → 24 saat; frontend'e gerçek 401→refresh→retry akışı eklendi (`AuthService.ensureValidToken`, `auth.interceptor.ts`) |
