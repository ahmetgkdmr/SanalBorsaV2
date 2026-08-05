namespace SanalBorsa.Application.Common;

/// <summary>
/// ABD hisseleri sanal alım-satım penceresi (ET — DST otomatik):
/// açık 16:00–ertesi gün 09:30 ET; kapalı 09:30–16:00 ET (NYSE seans saatleri).
/// Kapanış fiyatı netleştikten sonra işlem serbest — <see cref="BistTradingHours"/> ile aynı mantık.
/// </summary>
public static class NyseTradingHours
{
    public const string ClosedErrorCode = "US_CLOSED";

    public static readonly string ClosedMessage =
        "ABD hisse alım-satım penceresi şu an kapalı (NYSE seansı devam ediyor). Sanal portföyde " +
        "ABD hisse alım-satımı, günün kapanış fiyatı netleştikten sonra her gün 16:00 ile ertesi " +
        "sabah 09:30 arasında (New York saati) yapılabilir. Seans saatlerinde (09:30–16:00 ET) " +
        "fiyatlar henüz kesinleşmediği için işlem açılamaz.";

    public static bool IsOpen(DateTimeOffset? utcNow = null)
    {
        var nowUtc = utcNow ?? DateTimeOffset.UtcNow;
        var eastern = ResolveEasternTimeZone();
        var local = TimeZoneInfo.ConvertTime(nowUtc, eastern);
        var t = local.TimeOfDay;

        // 16:00 inclusive → 09:30 exclusive
        var openFrom = new TimeSpan(16, 0, 0);
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
