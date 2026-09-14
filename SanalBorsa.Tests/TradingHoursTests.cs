using FluentAssertions;
using SanalBorsa.Application.Common;

namespace SanalBorsa.Tests;

/// <summary>
/// Seans penceresi kuralı: sanal BIST işlemleri günün kapanışı netleştikten SONRA açılır
/// (19:00–09:30 TR). Sınır saatleri kritik — bir saat kayması, kullanıcıların henüz
/// kesinleşmemiş fiyatla işlem yapabilmesi demek.
/// </summary>
public class BistTradingHoursTests
{
    /// <summary>Verilen Türkiye saatini UTC'ye çevirir — testler yerel saat diliminden bağımsız olsun.</summary>
    private static DateTimeOffset TurkeyTime(int year, int month, int day, int hour, int minute)
    {
        var tz = BistTradingHours.ResolveTurkeyTimeZone();
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, tz.GetUtcOffset(local));
    }

    [Theory]
    [InlineData(19, 0)]   // açılış anı — dahil
    [InlineData(21, 30)]
    [InlineData(23, 59)]
    [InlineData(0, 0)]
    [InlineData(6, 15)]
    [InlineData(9, 29)]   // kapanıştan bir dakika önce — hâlâ açık
    public void Pencere_icinde_acik(int hour, int minute)
        => BistTradingHours.IsOpen(TurkeyTime(2026, 3, 10, hour, minute)).Should().BeTrue();

    [Theory]
    [InlineData(9, 30)]   // kapanış anı — hariç
    [InlineData(12, 0)]
    [InlineData(15, 45)]
    [InlineData(18, 59)]  // açılıştan bir dakika önce — hâlâ kapalı
    public void Pencere_disinda_kapali(int hour, int minute)
        => BistTradingHours.IsOpen(TurkeyTime(2026, 3, 10, hour, minute)).Should().BeFalse();

    [Fact]
    public void Yaz_saatinde_de_ayni_yerel_saatler_gecerli()
    {
        // Temmuz (UTC+3) ve Ocak (UTC+3) — Türkiye kalıcı UTC+3 kullanıyor, yine de
        // yerel saat üzerinden hesaplandığı doğrulanıyor.
        BistTradingHours.IsOpen(TurkeyTime(2026, 7, 15, 20, 0)).Should().BeTrue();
        BistTradingHours.IsOpen(TurkeyTime(2026, 1, 15, 20, 0)).Should().BeTrue();
        BistTradingHours.IsOpen(TurkeyTime(2026, 7, 15, 13, 0)).Should().BeFalse();
    }
}

public class NyseTradingHoursTests
{
    [Fact]
    public void Abd_hisselerinde_alim_satim_bilincli_olarak_kapali()
    {
        // Kurumsal olay verisi tek kaynaklı (Yahoo) ve spin-off/merger yakalanamıyor;
        // bu yüzden işlem bilinçli kapalı tutuluyor. Sessizce açılırsa test uyarır.
        NyseTradingHours.IsOpen().Should().BeFalse();
    }

    [Fact]
    public void Kapali_mesaji_kullaniciya_yol_gosteriyor()
    {
        var act = () => NyseTradingHours.EnsureOpen();
        act.Should().Throw<Exception>()
            .WithMessage("*BIST ve kripto*");
    }
}
