using FluentValidation;
using SanalBorsa.Application.Auth.Commands.CompleteRegistration;
using SanalBorsa.Application.Auth.Commands.LoginWithFirebase;
using SanalBorsa.Application.Auth.Commands.LoginWithPassword;
using SanalBorsa.Application.Auth.Commands.RefreshToken;
using SanalBorsa.Application.Auth.Commands.RegisterWithPassword;
using SanalBorsa.Application.Common;

namespace SanalBorsa.Application.Auth.Commands;

/// <summary>
/// Kimlik komutlarının biçim doğrulaması. Kullanıcı adının BENZERSİZLİĞİ burada değil
/// handler'da kontrol edilir — veritabanı gerektirir ve yarış durumuna karşı asıl güvence
/// zaten oradaki kontrol + tablo kısıtıdır.
/// </summary>
public sealed class RegisterWithPasswordCommandValidator
    : AbstractValidator<RegisterWithPasswordCommand>
{
    public RegisterWithPasswordCommandValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("Kullanıcı adı gerekli.")
            .Matches(ValidationRules.Username).WithMessage(ValidationRules.UsernameMessage);

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Şifre gerekli.")
            .MinimumLength(ValidationRules.PasswordMinLength)
            .WithMessage($"Şifre en az {ValidationRules.PasswordMinLength} karakter olmalı.");

        RuleFor(x => x.PasswordConfirm)
            .Equal(x => x.Password).WithMessage("Şifreler eşleşmiyor.");

        RuleFor(x => x.DisplayName)
            .MaximumLength(64).WithMessage("Görünen ad en fazla 64 karakter olabilir.")
            .When(x => !string.IsNullOrEmpty(x.DisplayName));
    }
}

public sealed class LoginWithPasswordCommandValidator : AbstractValidator<LoginWithPasswordCommand>
{
    public LoginWithPasswordCommandValidator()
    {
        RuleFor(x => x.Username).NotEmpty().WithMessage("Kullanıcı adı gerekli.");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Şifre gerekli.");
    }
}

public sealed class CompleteRegistrationCommandValidator
    : AbstractValidator<CompleteRegistrationCommand>
{
    public CompleteRegistrationCommandValidator()
    {
        RuleFor(x => x.IdToken).NotEmpty().WithMessage("Kimlik doğrulama token'ı gerekli.");

        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("Kullanıcı adı gerekli.")
            .Matches(ValidationRules.Username).WithMessage(ValidationRules.UsernameMessage);

        RuleFor(x => x.DisplayName)
            .MaximumLength(64).WithMessage("Görünen ad en fazla 64 karakter olabilir.")
            .When(x => !string.IsNullOrEmpty(x.DisplayName));
    }
}

public sealed class LoginWithFirebaseCommandValidator : AbstractValidator<LoginWithFirebaseCommand>
{
    public LoginWithFirebaseCommandValidator()
        => RuleFor(x => x.IdToken).NotEmpty().WithMessage("Kimlik doğrulama token'ı gerekli.");
}

public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
        => RuleFor(x => x.RefreshToken).NotEmpty().WithMessage("Yenileme token'ı gerekli.");
}
