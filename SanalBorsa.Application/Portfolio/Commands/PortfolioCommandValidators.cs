using FluentValidation;
using SanalBorsa.Application.Common;
using SanalBorsa.Application.Portfolio.Commands.BuyCrypto;
using SanalBorsa.Application.Portfolio.Commands.BuyStock;
using SanalBorsa.Application.Portfolio.Commands.BuyUsStock;
using SanalBorsa.Application.Portfolio.Commands.SellCrypto;
using SanalBorsa.Application.Portfolio.Commands.SellStock;
using SanalBorsa.Application.Portfolio.Commands.SellUsStock;

namespace SanalBorsa.Application.Portfolio.Commands;

/// <summary>
/// Alım/satım komutlarının BİÇİM doğrulaması. Buradaki kurallar veritabanına veya dış servise
/// bakmadan, sadece isteğin kendisine bakarak karar verilebilenlerdir; bu sayede handler'a hiç
/// girmeden, tek biçimli bir 400 (alan adı → hata listesi) olarak dönerler.
///
/// <para>
/// Durum gerektiren kurallar bilinçli olarak handler'da kaldı: bakiye yeterliliği, elde yeterli
/// lot olup olmadığı, seans saati, hissenin işleme kapalı olması. Onlar portföyü ve piyasayı
/// okumadan doğrulanamaz.
/// </para>
/// </summary>
internal static class TradeValidatorExtensions
{
    public static IRuleBuilderOptions<T, string> ValidSymbol<T>(
        this IRuleBuilder<T, string> rule)
        => rule
            .NotEmpty().WithMessage("Sembol gerekli.")
            .MaximumLength(32).WithMessage("Sembol en fazla 32 karakter olabilir.");

    public static IRuleBuilderOptions<T, Guid> ValidUserId<T>(
        this IRuleBuilder<T, Guid> rule)
        => rule.NotEmpty().WithMessage("Kullanıcı kimliği gerekli.");

    public static IRuleBuilderOptions<T, long> ValidLots<T>(
        this IRuleBuilder<T, long> rule)
        => rule
            .GreaterThan(0).WithMessage("Lot sayısı 0'dan büyük olmalıdır.")
            .LessThanOrEqualTo(ValidationRules.MaxLotsPerOrder)
            .WithMessage($"Tek emirde en fazla {ValidationRules.MaxLotsPerOrder:N0} lot işlem yapılabilir.");

    public static IRuleBuilderOptions<T, decimal> ValidAmount<T>(
        this IRuleBuilder<T, decimal> rule, string label)
        => rule
            .GreaterThan(0).WithMessage($"{label} 0'dan büyük olmalıdır.")
            .LessThanOrEqualTo(ValidationRules.MaxAmountPerOrder)
            .WithMessage($"Tek emirde en fazla {ValidationRules.MaxAmountPerOrder:N0} işlem yapılabilir.");
}

public sealed class BuyStockCommandValidator : AbstractValidator<BuyStockCommand>
{
    public BuyStockCommandValidator()
    {
        RuleFor(x => x.UserId).ValidUserId();
        RuleFor(x => x.Symbol).ValidSymbol();
        RuleFor(x => x.Lots).ValidLots();
    }
}

public sealed class SellStockCommandValidator : AbstractValidator<SellStockCommand>
{
    public SellStockCommandValidator()
    {
        RuleFor(x => x.UserId).ValidUserId();
        RuleFor(x => x.Symbol).ValidSymbol();
        RuleFor(x => x.Lots).ValidLots();
    }
}

public sealed class BuyUsStockCommandValidator : AbstractValidator<BuyUsStockCommand>
{
    public BuyUsStockCommandValidator()
    {
        RuleFor(x => x.UserId).ValidUserId();
        RuleFor(x => x.Symbol).ValidSymbol();
        RuleFor(x => x.TryAmount).ValidAmount("Tutar");
    }
}

public sealed class SellUsStockCommandValidator : AbstractValidator<SellUsStockCommand>
{
    public SellUsStockCommandValidator()
    {
        RuleFor(x => x.UserId).ValidUserId();
        RuleFor(x => x.Symbol).ValidSymbol();
        RuleFor(x => x.Quantity).ValidAmount("Miktar");
    }
}

/// <summary>
/// Kripto alımı üç farklı girdiden biriyle yapılabilir (TL tutarı, USD tutarı ya da adet);
/// tam olarak BİRİ verilmelidir — ikisi birden gelirse hangisinin esas alınacağı belirsiz kalır.
/// </summary>
public sealed class BuyCryptoCommandValidator : AbstractValidator<BuyCryptoCommand>
{
    public BuyCryptoCommandValidator()
    {
        RuleFor(x => x.UserId).ValidUserId();
        RuleFor(x => x.Symbol).ValidSymbol();

        RuleFor(x => x)
            .Must(HasExactlyOneInput)
            .WithName("amount")
            .WithMessage("tryAmount, quoteUsd veya quantity alanlarından tam olarak biri verilmelidir.");

        RuleFor(x => x.TryAmount!.Value).ValidAmount("Tutar").When(x => x.TryAmount.HasValue);
        RuleFor(x => x.QuoteUsd!.Value).ValidAmount("Tutar").When(x => x.QuoteUsd.HasValue);
        RuleFor(x => x.Quantity!.Value).ValidAmount("Miktar").When(x => x.Quantity.HasValue);
    }

    private static bool HasExactlyOneInput(BuyCryptoCommand c)
    {
        var provided = 0;
        if (c.TryAmount is > 0) provided++;
        if (c.QuoteUsd is > 0) provided++;
        if (c.Quantity is > 0) provided++;
        return provided == 1;
    }
}

public sealed class SellCryptoCommandValidator : AbstractValidator<SellCryptoCommand>
{
    public SellCryptoCommandValidator()
    {
        RuleFor(x => x.UserId).ValidUserId();
        RuleFor(x => x.Symbol).ValidSymbol();
        RuleFor(x => x.Quantity).ValidAmount("Miktar");
    }
}
