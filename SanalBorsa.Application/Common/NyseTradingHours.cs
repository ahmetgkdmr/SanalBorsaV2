namespace SanalBorsa.Application.Common;

/// <summary>
/// ABD hisseleri sanal alım-satım penceresi (ET — DST otomatik):
/// açık 16:25–ertesi gün 09:30 ET; kapalı 09:30–16:25 ET (NYSE seans saatleri).
/// Kapanış fiyatı netleştikten sonra işlem serbest — <see cref="BistTradingHours"/> ile aynı mantık.
/// 16:25, ham (16:10 ET) VE düzeltilmiş (16:20 ET) fiyat senkronlarının İKİSİ de bittikten sonra
/// gelecek şekilde seçildi — eskiden açılış 16:00'daydı, senkrondan (16:10/16:20) ÖNCE geliyordu,
/// bu da 16:00-16:20 arası bir önceki günün fiyatıyla işlem yapılabilmesi riski taşıyordu
/// (bkz. proje sohbeti — RecurringJobRegistrar'daki UsPriceSyncJob/UsCorporateActionSyncJob saatleri).
///
/// TradingEnabled = false — proje sohbeti: ABD hisselerinde kurumsal olay verisi tek kaynak
/// (Yahoo Finance), KAP gibi çapraz doğrulanabilir resmi bir kaynağımız yok; ayrıca spin-off/merger
/// gibi olayları hiç yakalayamıyoruz (gerçek zarara yol açabilir — bkz. proje sohbeti). Bu yüzden
/// alım-satım bilinçli olarak kapalı tutuluyor; saat penceresi mantığı ileride (güvenilir bir
/// kurumsal olay kaynağı bulunursa) tekrar açmak için olduğu gibi bırakıldı.
/// </summary>
public static class NyseTradingHours
{
    private const bool TradingEnabled = false;

    public const string ClosedErrorCode = "US_CLOSED";

    public static readonly string ClosedMessage =
        "Şu an sadece BIST ve kripto tarafında alım satım yapabilirsiniz.";

    public static bool IsOpen(DateTimeOffset? utcNow = null)
    {
        if (!TradingEnabled)
            return false;

        var nowUtc = utcNow ?? DateTimeOffset.UtcNow;
        var eastern = ResolveEasternTimeZone();
        var local = TimeZoneInfo.ConvertTime(nowUtc, eastern);
        var t = local.TimeOfDay;

        // 16:25 inclusive → 09:30 exclusive — bkz. sınıf üstü not (16:10/16:20 senkronlarından sonra).
        var openFrom = new TimeSpan(16, 25, 0);
        var openUntil = new TimeSpan(9, 30, 0);
        return t >= openFrom || t < openUntil;
    }

    public static void EnsureOpen()
    {
        if (!IsOpen())
            throw new InvalidOperationException($"[{ClosedErrorCode}] {ClosedMessage}");
    }

    /// <summary>Hangfire cron kaydında (RecurringJobRegistrar) da reuse edilir.</summary>
    public static TimeZoneInfo ResolveEasternTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows() ? "Eastern Standard Time" : "America/New_York");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        }
    }
}
