namespace SanalBorsa.Application.Common;

/// <summary>
/// BIST sanal alım-satım penceresi (Türkiye saati):
/// açık 18:45–ertesi gün 10:00; kapalı 10:00–18:45.
/// Kapanış fiyatı 18:30’da netleştikten sonra işlem serbest.
/// </summary>
public static class BistTradingHours
{
    public const string ClosedErrorCode = "BIST_CLOSED";

    public static readonly string ClosedMessage =
        "Borsa İstanbul işlemleri şu an kapalı. " +
        "Sanal portföyde BIST alım-satımı, günün kapanış fiyatı netleştikten sonra " +
        "her gün 18:45 ile ertesi sabah 10:00 arasında (Türkiye saati) yapılabilir. " +
        "Seans saatlerinde (10:00–18:45) fiyatlar henüz kesinleşmediği için işlem açılamaz. " +
        "Kripto işlemleri 7/24 açıktır.";

    public static bool IsOpen(DateTimeOffset? utcNow = null)
    {
        var nowUtc = utcNow ?? DateTimeOffset.UtcNow;
        var turkey = ResolveTurkeyTimeZone();
        var local = TimeZoneInfo.ConvertTime(nowUtc, turkey);
        var t = local.TimeOfDay;

        // 18:45 inclusive → 10:00 exclusive
        var openFrom = new TimeSpan(18, 45, 0);
        var openUntil = new TimeSpan(10, 0, 0);
        return t >= openFrom || t < openUntil;
    }

    public static void EnsureOpen()
    {
        if (!IsOpen())
            throw new InvalidOperationException($"[{ClosedErrorCode}] {ClosedMessage}");
    }

    private static TimeZoneInfo ResolveTurkeyTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows() ? "Turkey Standard Time" : "Europe/Istanbul");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        }
    }
}
