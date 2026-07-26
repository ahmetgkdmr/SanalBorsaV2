namespace SanalBorsa.Application.DTOs;

public record LotEventMarkerDto(
    int Year,
    int Month,
    string ActionDateLabel,
    string ActionType,
    string Label,
    decimal LotsBefore,
    decimal LotsAfter,
    string? Description,
    decimal? CashReceived = null,
    decimal? LotsBought = null,
    string? Story = null,
    /// <summary>Olayın gerçek günü — günlük grafikte noktayı tam yerine koymak için.</summary>
    int Day = 0);
