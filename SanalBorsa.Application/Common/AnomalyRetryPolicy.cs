namespace SanalBorsa.Application.Common;

/// <summary>
/// Fiyat tutarsızlığı tespit edildiğinde ("düzeltilmiş fiyat ham ile çelişiyor" ya da "ham fiyat
/// bir günde %20+ sıçradı") kaynağa (TradingView) ne sıklıkla tekrar sorulacağını belirler.
/// İki aşamalı: önce sık (5 dk × 50 deneme, ~4 saat 10 dk), sonra seyrek (15 dk × 20 deneme daha,
/// ~5 saat) — toplam ~9 saat boyunca kaynağın kendini düzeltmesi için şans tanınır (bkz. proje
/// sohbeti: ROK/HUBB'ta TradingView birkaç dakika/saat içinde kendini düzeltmişti). Bu süre sonunda
/// hâlâ düzelmemişse pes edilir; hisse bir sonraki BAŞARILI (şüphesiz) senkrona kadar alım/satıma
/// kapalı kalır (bkz. Stock.TradingHaltReason).
/// </summary>
public static class AnomalyRetryPolicy
{
    public const int Phase1MaxAttempts = 50;
    public static readonly TimeSpan Phase1Delay = TimeSpan.FromMinutes(5);

    public const int Phase2MaxAttempts = 20;
    public static readonly TimeSpan Phase2Delay = TimeSpan.FromMinutes(15);

    public const int MaxAttempts = Phase1MaxAttempts + Phase2MaxAttempts;

    /// <param name="attempt">Şu ana kadar yapılmış deneme sayısı (bir sonraki deneme attempt+1 olacak).</param>
    public static bool ShouldGiveUp(int attempt) => attempt >= MaxAttempts;

    /// <summary>Bir sonraki deneme için beklenecek süre.</summary>
    public static TimeSpan NextDelay(int attempt) => attempt < Phase1MaxAttempts ? Phase1Delay : Phase2Delay;
}
