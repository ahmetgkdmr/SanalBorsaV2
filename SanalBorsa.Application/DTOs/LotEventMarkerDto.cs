namespace SanalBorsa.Application.DTOs;

public record LotEventMarkerDto(
    int Year,
    int Month,
    string ActionDateLabel,
    string ActionType,
    string Label,
    long LotsBefore,
    long LotsAfter,
    string? Description);
