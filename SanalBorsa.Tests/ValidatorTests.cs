using FluentAssertions;
using SanalBorsa.Application.Auth.Commands;
using SanalBorsa.Application.Auth.Commands.RegisterWithPassword;
using SanalBorsa.Application.Portfolio.Commands;
using SanalBorsa.Application.Portfolio.Commands.BuyCrypto;
using SanalBorsa.Application.Portfolio.Commands.BuyStock;

namespace SanalBorsa.Tests;

public class PortfolioValidatorTests
{
    private readonly BuyStockCommandValidator _buyStock = new();
    private readonly BuyCryptoCommandValidator _buyCrypto = new();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Lot_sifir_veya_negatif_olamaz(long lots)
    {
        var result = _buyStock.Validate(new BuyStockCommand(Guid.NewGuid(), "THYAO", lots));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Lots");
    }

    [Fact]
    public void Sembol_bos_olamaz()
    {
        var result = _buyStock.Validate(new BuyStockCommand(Guid.NewGuid(), "", 10));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Symbol");
    }

    [Fact]
    public void Gecerli_alim_dogrulamayi_gecer()
    {
        var result = _buyStock.Validate(new BuyStockCommand(Guid.NewGuid(), "THYAO", 10));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Asiri_buyuk_lot_reddedilir()
    {
        // Fazladan sıfır yazma hatasına karşı fren.
        var result = _buyStock.Validate(new BuyStockCommand(Guid.NewGuid(), "THYAO", long.MaxValue));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Kripto_aliminda_hicbir_girdi_verilmezse_reddedilir()
    {
        var result = _buyCrypto.Validate(
            new BuyCryptoCommand(Guid.NewGuid(), "BTCUSDT", null, null, null));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Kripto_aliminda_iki_girdi_birden_verilemez()
    {
        // tryAmount ve quantity aynı anda gelirse hangisinin esas alınacağı belirsiz kalır.
        var result = _buyCrypto.Validate(
            new BuyCryptoCommand(Guid.NewGuid(), "BTCUSDT", TryAmount: 1000m, QuoteUsd: null, Quantity: 0.5m));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Kripto_aliminda_tek_girdi_kabul_edilir()
    {
        var result = _buyCrypto.Validate(
            new BuyCryptoCommand(Guid.NewGuid(), "BTCUSDT", TryAmount: 1000m, QuoteUsd: null, Quantity: null));
        result.IsValid.Should().BeTrue();
    }
}

public class AuthValidatorTests
{
    private readonly RegisterWithPasswordCommandValidator _register = new();

    [Theory]
    [InlineData("ab")]              // çok kısa
    [InlineData("1ahmet")]          // rakamla başlıyor
    [InlineData("ahmet-gokdemir")]  // tire geçersiz
    [InlineData("ahmet gokdemir")]  // boşluk geçersiz
    public void Gecersiz_kullanici_adlari_reddedilir(string username)
    {
        var result = _register.Validate(
            new RegisterWithPasswordCommand(username, "sifre123", "sifre123"));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Username");
    }

    [Theory]
    [InlineData("ahmet")]
    [InlineData("ahmet_gokdemir")]
    [InlineData("a1b2c3")]
    public void Gecerli_kullanici_adlari_kabul_edilir(string username)
    {
        var result = _register.Validate(
            new RegisterWithPasswordCommand(username, "sifre123", "sifre123"));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Kisa_sifre_reddedilir()
    {
        var result = _register.Validate(new RegisterWithPasswordCommand("ahmet", "123", "123"));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Password");
    }

    [Fact]
    public void Eslesmeyen_sifreler_reddedilir()
    {
        var result = _register.Validate(
            new RegisterWithPasswordCommand("ahmet", "sifre123", "sifre456"));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "PasswordConfirm");
    }
}
