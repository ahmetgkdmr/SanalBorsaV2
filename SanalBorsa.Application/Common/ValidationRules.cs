using System.Text.RegularExpressions;

namespace SanalBorsa.Application.Common;

/// <summary>
/// Validator'ların paylaştığı sabitler. Kullanıcı adı deseni gibi kurallar birden fazla
/// komutta geçtiği için tek yerde tutuluyor — kayıt ve profil tamamlama akışlarının
/// zamanla birbirinden ayrışmasını önler.
/// </summary>
public static class ValidationRules
{
    /// <summary>Harfle başlar, 3-32 karakter, harf/rakam/alt çizgi.</summary>
    public static readonly Regex Username = new(
        @"^[a-zA-Z][a-zA-Z0-9_]{2,31}$",
        RegexOptions.Compiled);

    public const string UsernameMessage =
        "Kullanıcı adı harfle başlamalı; 3-32 karakter, sadece harf, rakam ve alt çizgi içerebilir.";

    public const int PasswordMinLength = 6;

    /// <summary>Tek bir emirde izin verilen üst sınır — hatalı girişte (fazladan sıfır) fren.</summary>
    public const long MaxLotsPerOrder = 100_000_000;

    public const decimal MaxAmountPerOrder = 1_000_000_000m;
}
