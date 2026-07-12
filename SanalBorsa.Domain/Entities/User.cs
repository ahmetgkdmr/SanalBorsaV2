namespace SanalBorsa.Domain.Entities;

public class User
{
    public Guid Id { get; set; }

    /// <summary>Unique Firebase UID (sub claim) from any provider.</summary>
    public string FirebaseUid { get; set; } = string.Empty;

    /// <summary>Auth provider used at first registration.</summary>
    public AuthProvider Provider { get; set; }

    /// <summary>Display name (from Google profile or generated from phone).</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Email address — populated for Google logins.</summary>
    public string? Email { get; set; }

    /// <summary>Phone number in E.164 format — populated for phone logins.</summary>
    public string? PhoneNumber { get; set; }

    public bool EmailVerified { get; set; }

    public bool PhoneVerified { get; set; }

    public string? AvatarUrl { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public UserPortfolio? Portfolio { get; set; }
}

public enum AuthProvider
{
    Google = 1,
    Phone  = 2,
}
