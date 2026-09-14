using FluentAssertions;
using SanalBorsa.Application.Common.Constants;
using SanalBorsa.Application.Mappings;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Enums;

namespace SanalBorsa.Tests;

/// <summary>
/// AutoMapper kaldırılıp yerine açık uzantı metotları geldi; dönüşümlerin alan alan doğru
/// kaldığını burada sabitliyoruz (özellikle piyasa tipi eşlemesi ve null davranışı).
/// </summary>
public class EntityMappingTests
{
    [Fact]
    public void Stock_dto_alanlari_dogru_esleniyor()
    {
        var stock = new Stock
        {
            Id = 7,
            Symbol = "THYAO",
            Name = "Türk Hava Yolları",
            Sector = "Ulaştırma",
            Industry = "Havayolu",
            Currency = "TRY",
            Exchange = "BIST",
            IsActive = true,
            MarketType = MarketType.Bist,
            EarliestDataDate = new DateTime(1993, 8, 11),
            LatestDataDate = new DateTime(2026, 9, 1),
            NeedsHistoryRefresh = false,
        };

        var dto = stock.ToDto();

        dto.Id.Should().Be(7);
        dto.Symbol.Should().Be("THYAO");
        dto.Name.Should().Be("Türk Hava Yolları");
        dto.Currency.Should().Be("TRY");
        dto.MarketType.Should().Be("bist");
        dto.EarliestDataDate.Should().Be(new DateTime(1993, 8, 11));
        // Fiyat alanları handler'lar tarafından `with` ile dolduruluyor, mapping'de null kalmalı.
        dto.LastClose.Should().BeNull();
        dto.Sparkline.Should().BeNull();
    }

    [Fact]
    public void Kripto_piyasa_tipi_crypto_olarak_esleniyor()
    {
        var stock = new Stock { Symbol = "BTCUSDT", Name = "Bitcoin", MarketType = MarketType.Crypto };
        stock.ToDto().MarketType.Should().Be("crypto");
    }

    [Fact]
    public void Fiyat_gecmisi_dto_alanlari_dogru_esleniyor()
    {
        var bar = new StockPriceHistory
        {
            Date = new DateTime(2026, 8, 10),
            Open = 307m,
            High = 307.75m,
            Low = 302.25m,
            Close = 303m,
            AdjustedClose = 299m,
            Volume = 42_097_483,
        };

        var dto = bar.ToDto();

        dto.Date.Should().Be(new DateTime(2026, 8, 10));
        dto.Open.Should().Be(307m);
        dto.Close.Should().Be(303m);
        dto.AdjustedClose.Should().Be(299m);
        dto.Volume.Should().Be(42_097_483);
    }

    [Fact]
    public void Kurumsal_islem_iliskili_hisse_yoksa_bos_sembol_doner()
    {
        var action = new CorporateAction
        {
            Id = 1,
            ActionType = CorporateActionType.Dividend,
            ActionDate = new DateTime(2025, 9, 2),
            Value = 1.5m,
        };

        var dto = action.ToDto();

        dto.Symbol.Should().BeEmpty();
        dto.ActionTypeName.Should().Be("Dividend");
        dto.Value.Should().Be(1.5m);
    }
}

/// <summary>
/// Asgari ücret tablosu, zaman makinesinde hem varsayılan tutarın hem de "bugünün parasıyla"
/// ipucunun çıpası — dönem sınırlarının doğru seçildiğini garantiliyoruz.
/// </summary>
public class MinimumWageTests
{
    [Fact]
    public void Donem_basindan_onceki_tarih_onceki_donemin_ucretini_verir()
    {
        // 1993-08-01'de yeni dönem başlıyor; 31 Temmuz hâlâ önceki dönem.
        var before = MinimumWageByYear.Get(new DateTime(1993, 7, 31));
        var after = MinimumWageByYear.Get(new DateTime(1993, 8, 1));

        after.Should().BeGreaterThan(before);
    }

    [Fact]
    public void Ucret_yillar_icinde_artiyor()
    {
        var y1995 = MinimumWageByYear.Get(new DateTime(1995, 6, 1));
        var y2010 = MinimumWageByYear.Get(new DateTime(2010, 6, 1));
        var y2026 = MinimumWageByYear.Get(new DateTime(2026, 6, 1));

        y2010.Should().BeGreaterThan(y1995);
        y2026.Should().BeGreaterThan(y2010);
    }

    [Fact]
    public void Tablodan_cok_onceki_tarih_ilk_donemi_verir()
    {
        var wage = MinimumWageByYear.Get(new DateTime(1950, 1, 1));
        wage.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Iki_bin_bes_oncesi_degerler_yeni_tl_biriminde_saklanir()
    {
        // 1993 asgari ücreti gerçekte 1.563.473 TL idi; seri ile aynı birimde olsun diye
        // 1.000.000'a bölünmüş halde tutuluyor.
        var wage = MinimumWageByYear.Get(new DateTime(1993, 8, 15));
        wage.Should().BeLessThan(10m);
        (wage * 1_000_000m).Should().BeGreaterThan(1_000_000m);
    }
}
