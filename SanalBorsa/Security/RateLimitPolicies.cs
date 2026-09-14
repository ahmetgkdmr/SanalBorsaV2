namespace SanalBorsa.API.Security;

/// <summary>Rate limit politika adları — dize tekrarını ve yazım hatasını önlemek için.</summary>
public static class RateLimitPolicies
{
    /// <summary>Giriş / kayıt / token yenileme uçları (IP başına dakikada 10 istek).</summary>
    public const string Auth = "auth";
}
