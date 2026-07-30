namespace SanalBorsa.Domain.Entities;

public class User
{
    public Guid Id { get; set; }

    /// <summary>Firebase UID — Google/telefon hesaplarında dolu; form (şifre) hesaplarında null.</summary>
    public string? FirebaseUid { get; set; }

    /// <summary>Auth provider used at first registration.</summary>
    public AuthProvider Provider { get; set; }

    /// <summary>
    /// Benzersiz kullanıcı adı (handle) — zorunlu, kullanıcı seçer.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Görünen ad — opsiyonel; boşsa Username gösterilir.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Email address — populated for Google logins.</summary>
    public string? Email { get; set; }

    /// <summary>Phone number in E.164 format — populated for phone logins.</summary>
    public string? PhoneNumber { get; set; }

    public bool EmailVerified { get; set; }

    public bool PhoneVerified { get; set; }

    public string? AvatarUrl { get; set; }

    /// <summary>Form kaydı için PBKDF2 hash. Google kullanıcılarında null.</summary>
    public string? PasswordHash { get; set; }

    /// <summary>
    /// true ise liderlik / profilde işlem geçmişi başkalarına görünür.
    /// Varsayılan: true.
    /// </summary>
    public bool ShowTradeHistoryPublic { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public UserPortfolio? Portfolio { get; set; }
}

public enum AuthProvider
{
    Google = 1,
    Phone  = 2,
    Local  = 3,
}
