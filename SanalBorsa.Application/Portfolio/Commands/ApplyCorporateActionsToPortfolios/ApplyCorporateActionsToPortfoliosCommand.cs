using MediatR;

namespace SanalBorsa.Application.Portfolio.Commands.ApplyCorporateActionsToPortfolios;

/// <summary>
/// Henüz portföylere uygulanmamış bedelsiz/bedelli/temettü olaylarını, o hisseyi ex-date'te
/// (kurumsal olay tarihinde) elinde tutan tüm kullanıcı portföylerine uygular — bkz. handler'ın
/// başındaki açıklama.
/// </summary>
public record ApplyCorporateActionsToPortfoliosCommand : IRequest<ApplyCorporateActionsToPortfoliosResult>;

public record ApplyCorporateActionsToPortfoliosResult(
    int ActionsScanned,
    int PortfolioApplications,
    int PortfolioSkippedNotEligible,
    int Failed);
