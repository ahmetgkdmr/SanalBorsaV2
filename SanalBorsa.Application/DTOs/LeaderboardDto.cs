namespace SanalBorsa.Application.DTOs;

public record LeaderboardDto(
    IReadOnlyList<LeaderboardEntryDto> Entries,
    /// <summary>Sıralamanın hesaplandığı an — istemci "şu kadar önce güncellendi" gösterebilsin.</summary>
    DateTime ComputedAt,
    /// <summary>Portföyü olan toplam kullanıcı sayısı (Take ile kırpılmadan önce).</summary>
    int TotalParticipants);

public record LeaderboardEntryDto(
    int Rank,
    string Username,
    string DisplayName,
    string? AvatarUrl,
    decimal PortfolioValue,
    decimal GainPct,
    /// <summary>
    /// Kullanıcı işlem geçmişini herkese açık yaptıysa true. Liste bu bilgiyi taşır ama
    /// işlemleri TAŞIMAZ — geçmiş ayrı bir uçtan, sadece izin verenler için çekilir.
    /// </summary>
    bool TradeHistoryPublic,
    int HoldingCount);
