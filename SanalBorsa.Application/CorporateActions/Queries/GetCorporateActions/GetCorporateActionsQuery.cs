using MediatR;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Enums;

namespace SanalBorsa.Application.CorporateActions.Queries.GetCorporateActions;

public record GetCorporateActionsQuery(
    string Symbol,
    CorporateActionType? ActionType = null
) : IRequest<IReadOnlyList<CorporateActionDto>>;
