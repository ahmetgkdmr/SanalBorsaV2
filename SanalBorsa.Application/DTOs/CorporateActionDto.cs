using SanalBorsa.Domain.Enums;

namespace SanalBorsa.Application.DTOs;

public record CorporateActionDto(
    int Id,
    string Symbol,
    CorporateActionType ActionType,
    string ActionTypeName,
    DateTime ActionDate,
    decimal Value,
    string? Description,
    DateTime CreatedAt
);
