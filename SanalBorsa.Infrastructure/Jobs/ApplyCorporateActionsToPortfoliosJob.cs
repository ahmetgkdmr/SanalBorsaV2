using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Portfolio.Commands.ApplyCorporateActionsToPortfolios;

namespace SanalBorsa.Infrastructure.Jobs;

/// <summary>
/// Her gün 10:15 Türkiye saati — bedelsiz/bedelli/temettü olaylarını, o hisseyi hak eden portföylere
/// uygular (bkz. ApplyCorporateActionsToPortfoliosCommandHandler). 10:15 seçilme nedeni — proje
/// sohbeti: BIST sanal seansımız 09:30 TR'de kapanıyor (bkz. BistTradingHours), o andan itibaren
/// hak sahipliği artık DEĞİŞEMEZ (kimse satıp hakkını kaybedemez) — 10:15, o kapanıştan ~45dk
/// sonrasını vererek güvenli bir tampon bırakıyor, aynı zamanda kullanıcı aynı gün içinde hesabına
/// yansımış görüyor (bir sonraki akşamın 18:35 senkronunu beklemek yerine).
/// Hangfire recurring job; kayıt: <see cref="RecurringJobRegistrar"/>.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 600)]
[AutomaticRetry(Attempts = 3)]
public sealed class ApplyCorporateActionsToPortfoliosJob
{
    private readonly IMediator _mediator;
    private readonly ILogger<ApplyCorporateActionsToPortfoliosJob> _logger;

    public ApplyCorporateActionsToPortfoliosJob(IMediator mediator, ILogger<ApplyCorporateActionsToPortfoliosJob> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("ApplyCorporateActionsToPortfoliosJob started at {Time}", DateTimeOffset.UtcNow);

        try
        {
            var result = await _mediator.Send(new ApplyCorporateActionsToPortfoliosCommand(), ct);

            _logger.LogInformation(
                "ApplyCorporateActionsToPortfoliosJob completed — ActionsScanned={Scanned} Applied={Applied} SkippedNotEligible={Skipped} Failed={Failed}",
                result.ActionsScanned, result.PortfolioApplications, result.PortfolioSkippedNotEligible, result.Failed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ApplyCorporateActionsToPortfoliosJob failed");
            throw;
        }
    }
}
