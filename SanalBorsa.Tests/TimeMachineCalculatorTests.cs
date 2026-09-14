using FluentAssertions;
using SanalBorsa.Application.Common.Services;
using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Tests;

/// <summary>
/// Zaman makinesinin para hesabı. Burası projenin en çok hata çıkan yeri oldu (kur/birim
/// karışıklıkları, lot sayısının yanlış paydaya bölünmesi), o yüzden sözleşmesi testle
/// sabitleniyor: <b>yatırılan × (bugünküDüzeltilmiş / alımGünüDüzeltilmiş) = bugünkü değer</b>
/// ve <b>lot = değer / bugünkü ham fiyat</b>.
/// </summary>
public class TimeMachineCalculatorTests
{
    private static StockPriceHistory Bar(string date, decimal close, decimal? adjusted = null) => new()
    {
        Date = DateTime.Parse(date),
        Open = close,
        High = close,
        Low = close,
        Close = close,
        AdjustedClose = adjusted ?? close,
        Volume = 1_000,
    };

    /// <summary>Aylık nokta üretebilmesi için seri en az birkaç ay sürmeli.</summary>
    private static List<StockPriceHistory> Series(decimal start, decimal end)
    {
        var bars = new List<StockPriceHistory>();
        var from = new DateTime(2020, 1, 15);
        var to = new DateTime(2024, 1, 15);
        var totalDays = (to - from).Days;

        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var t = (decimal)(d - from).Days / totalDays;
            var price = start + (end - start) * t;
            bars.Add(Bar(d.ToString("yyyy-MM-dd"), price));
        }

        return bars;
    }

    [Fact]
    public void Lump_para_ikiye_katlandiginda_deger_de_ikiye_katlanir()
    {
        var prices = Series(start: 10m, end: 20m);

        var result = TimeMachineCalculator.Calculate(
            symbol: "TEST",
            prices: prices,
            actions: [],
            buyDate: new DateTime(2020, 1, 15),
            wagePercentage: 0,
            mode: "lump",
            amount: 1_000m);

        result.Error.Should().BeNull();
        result.Invested.Should().Be(1_000m);
        result.CurrentValue.Should().BeApproximately(2_000m, 1m);
        result.GainPct.Should().BeApproximately(100m, 0.5m);
    }

    [Fact]
    public void Lot_sayisi_bugunku_fiyata_bolunerek_bulunur()
    {
        // Dosya başındaki nota göre lot HER ZAMAN bugünün (güvenilir) ham fiyatına bölünür;
        // geçmişin ham fiyatına güvenilmiyor.
        var prices = Series(start: 10m, end: 20m);

        var result = TimeMachineCalculator.Calculate(
            "TEST", prices, [], new DateTime(2020, 1, 15), 0, "lump", amount: 1_000m);

        var expectedLots = result.CurrentValue / result.CurrentPrice;
        result.Lots.Should().BeApproximately(expectedLots, 0.01m);
    }

    [Fact]
    public void Duzeltilmis_kapanis_orani_esas_alinir_ham_fiyat_degil()
    {
        // Ham fiyat yarıya düşerken (2'ye bölünme) düzeltilmiş seri iki katına çıkıyor:
        // para hesabı düzeltilmiş seriyi izlemeli, yani DEĞER ARTMALI.
        var prices = new List<StockPriceHistory>();
        var from = new DateTime(2020, 1, 15);
        for (var d = from; d <= new DateTime(2024, 1, 15); d = d.AddDays(1))
        {
            var afterSplit = d >= new DateTime(2022, 1, 1);
            var raw = afterSplit ? 10m : 20m;      // bölünme ham fiyatı yarıya indirdi
            var adj = afterSplit ? 40m : 20m;      // gerçek getiri ise iki katına çıktı
            prices.Add(Bar(d.ToString("yyyy-MM-dd"), raw, adj));
        }

        var result = TimeMachineCalculator.Calculate(
            "SPLIT", prices, [], from, 0, "lump", amount: 1_000m);

        result.Error.Should().BeNull();
        result.CurrentValue.Should().BeApproximately(2_000m, 1m);
        result.GainPct.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Veri_baslangicindan_onceki_tarih_hata_dondurur()
    {
        var prices = Series(10m, 20m);

        var result = TimeMachineCalculator.Calculate(
            "TEST", prices, [], new DateTime(2019, 1, 1), 0, "lump", amount: 1_000m);

        result.Error.Should().NotBeNull();
        result.Error.Should().Contain("2020");
    }

    [Fact]
    public void Bos_fiyat_serisi_hata_dondurur()
    {
        var result = TimeMachineCalculator.Calculate(
            "TEST", [], [], new DateTime(2020, 1, 15), 0, "lump", amount: 1_000m);

        result.Error.Should().NotBeNull();
    }

    [Fact]
    public void Bist_disi_piyasada_tutar_zorunludur()
    {
        // ABD/kripto için "asgari ücret" çıpası anlamsız; amount verilmezse hata dönmeli.
        var result = TimeMachineCalculator.Calculate(
            "AAPL", Series(10m, 20m), [], new DateTime(2020, 1, 15),
            wagePercentage: 50, mode: "lump", amount: null, market: MarketType.UsStocks);

        result.Error.Should().NotBeNull();
        result.Error.Should().Contain("tutar");
    }

    [Fact]
    public void Hafta_sonu_secilirse_onceki_islem_gunune_kayar()
    {
        // Seride sadece hafta içi barlar var; Pazar seçildiğinde Cuma'nın kapanışı kullanılmalı
        // (ileriye değil GERİYE bakılır).
        var prices = new List<StockPriceHistory>();
        for (var d = new DateTime(2020, 1, 1); d <= new DateTime(2024, 1, 15); d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            prices.Add(Bar(d.ToString("yyyy-MM-dd"), 10m));
        }

        // 2020-01-05 bir Pazar.
        var result = TimeMachineCalculator.Calculate(
            "TEST", prices, [], new DateTime(2020, 1, 5), 0, "lump", amount: 1_000m);

        result.Error.Should().BeNull();
        result.BuyPrice.Should().Be(10m);
    }

    [Fact]
    public void Tutar_hisse_almaya_yetmiyorsa_anlamli_hata_doner()
    {
        var prices = Series(start: 1_000_000m, end: 1_000_000m);

        var result = TimeMachineCalculator.Calculate(
            "PAHALI", prices, [], new DateTime(2020, 1, 15), 0, "lump", amount: 1m);

        result.Error.Should().NotBeNull();
        result.Error.Should().Contain("Tutarı artır");
    }

    [Fact]
    public void Dca_modunda_yatirilan_tutar_aylik_katkilarin_toplamidir()
    {
        var prices = Series(10m, 20m);

        var result = TimeMachineCalculator.Calculate(
            "TEST", prices, [], new DateTime(2020, 1, 15), 0, "dca", amount: 100m);

        result.Error.Should().BeNull();
        // 2020-01'den 2024-01'e ~48 ay; her ay 100 ₺ → tek seferlik 100 ₺'den çok daha fazla.
        result.Invested.Should().BeGreaterThan(1_000m);
        result.CurrentValue.Should().BeGreaterThan(result.Invested);
    }

    [Fact]
    public void Hikaye_satirlari_uretiliyor()
    {
        var result = TimeMachineCalculator.Calculate(
            "TEST", Series(10m, 20m), [], new DateTime(2020, 1, 15), 0, "lump", amount: 1_000m);

        result.StoryLines.Should().NotBeNull();
        result.StoryLines!.Should().NotBeEmpty();
        result.StoryLines![0].Should().Contain("lotun olurdu");
    }
}
