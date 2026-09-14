using SanalBorsa.Application.Common.Exceptions;

namespace SanalBorsa.Application.Common;

/// <summary>
/// BIST sanal alım-satım penceresi (Türkiye saati):
/// açık 19:00–ertesi gün 09:30; kapalı 09:30–19:00.
/// Kapanış fiyatı 18:30’da netleştikten sonra işlem serbest.
/// </summary>
public static class BistTradingHours
{
    public const string ClosedErrorCode = "BIST_CLOSED";

    public static readonly string ClosedMessage =
        "Borsa İstanbul işlemleri şu an kapalı. " +
        "Sanal portföyde BIST alım-satımı, günün kapanış fiyatı netleştikten sonra " +
        "her gün 19:00 ile ertesi sabah 09:30 arasında (Türkiye saati) yapılabilir. " +
        "Seans saatlerinde (09:30–19:00) fiyatlar henüz kesinleşmediği için işlem açılamaz. " +
        "Kripto işlemleri 7/24 açıktır.";

    public static bool IsOpen(DateTimeOffset? utcNow = null)
    {
        var nowUtc = utcNow ?? DateTimeOffset.UtcNow;
        var turkey = ResolveTurkeyTimeZone();
        var local = TimeZoneInfo.ConvertTime(nowUtc, turkey);
        var t = local.TimeOfDay;

        // 19:00 inclusive → 09:30 exclusive
        var openFrom = new TimeSpan(19, 0, 0);
        var openUntil = new TimeSpan(9, 30, 0);
        return t >= openFrom || t < openUntil;
    }

    public static void EnsureOpen(DateTimeOffset? utcNow = null)
    {
        if (!IsOpen(utcNow))
            throw new BusinessRuleException($"[{ClosedErrorCode}] {ClosedMessage}");
    }

    public static TimeZoneInfo ResolveTurkeyTimeZone()
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
