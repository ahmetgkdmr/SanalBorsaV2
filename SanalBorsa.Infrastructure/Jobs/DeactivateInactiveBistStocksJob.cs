using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Stocks.Commands.ComputeTopGainers;
using SanalBorsa.Application.Stocks.Commands.DeactivateInactiveBistStocks;
using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Infrastructure.Jobs;

/// <summary>
/// Admin-tetiklemeli: TV'den son N günde bar alınamayan BIST hisselerini soft-pasife çeker;
/// en az bir hisse pasife çekildiyse BIST top-gainers'ı da yeniden hesaplar.
/// <see cref="SanalBorsa.API.Controllers.StocksController.DeactivateInactive"/> tarafından
/// Hangfire job olarak enqueue edilir.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 600)]
[AutomaticRetry(Attempts = 2)]
public sealed class DeactivateInactiveBistStocksJob
{
    private readonly IMediator _mediator;
    private readonly ILogger<DeactivateInactiveBistStocksJob> _logger;

    public DeactivateInactiveBistStocksJob(IMediator mediator, ILogger<DeactivateInactiveBistStocksJob> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task RunAsync(int lookbackDays, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new DeactivateInactiveBistStocksCommand(lookbackDays), ct);
        _logger.LogInformation(
            "BIST inactive deactivate finished — checked={C} deactivated={D} probeFail={F} symbols=[{S}]",
            result.Checked, result.Deactivated, result.FailedProbe, string.Join(',', result.DeactivatedSymbols));

        if (result.Deactivated > 0)
        {
            await _mediator.Send(new ComputeTopGainersCommand(MarketType.Bist), ct);
            _logger.LogInformation("TopGainers recomputed after BIST soft-deactivate");
        }
    }
}
