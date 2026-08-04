using Hangfire;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Application.Indices.Commands.SyncParityHistory;
using SanalBorsa.Application.Stocks.Commands.ComputeTimeMachineLeaders;
using SanalBorsa.Application.Stocks.Queries.GetTimeMachineLeaders;
using SanalBorsa.Domain.Enums;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.API.Controllers;

[ApiController]
[Route("api/time-machine")]
[Produces("application/json")]
public class TimeMachineController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IBackgroundJobClient _jobs;

    public TimeMachineController(IMediator mediator, IBackgroundJobClient jobs)
    {
        _mediator = mediator;
        _jobs = jobs;
    }

    /// <summary>
    /// Verilen tarihten bugüne en çok kazandıran 5 BIST hissesi, 5 kripto ve
    /// USD/TRY · EUR/TRY · gram altın getirileri. Tarih işlem günü değilse
    /// önceki en yakın işlem gününe kayar.
    /// </summary>
    [HttpGet("leaders")]
    [ProducesResponseType(typeof(TimeMachineLeadersDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLeaders([FromQuery] DateTime date, CancellationToken ct)
        => Ok(await _mediator.Send(new GetTimeMachineLeadersQuery(date), ct));

    /// <summary>Liderlik tablosunun kategori bazlı özeti (satır sayısı, tarih aralığı).</summary>
    [HttpGet("leaders/stats")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStats([FromServices] IUnitOfWork uow, CancellationToken ct)
        => Ok(await uow.TimeMachineLeaders.GetStatsAsync(ct));

    /// <summary>
    /// Liderlik tablosunu yeniden hesaplar (manuel tetik). category=all|bist|crypto|parity.
    /// Uzun sürdüğü için arka planda çalışır.
    /// </summary>
    [HttpPost("leaders/compute")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public IActionResult Compute([FromQuery] string category = "all")
    {
        var parsed = ParseCategory(category);
        var jobId = _jobs.Enqueue<IMediator>(
            m => m.Send(new ComputeTimeMachineLeadersCommand(parsed), CancellationToken.None));

        return Accepted(new
        {
            message = "Time-machine leaders compute started in background.",
            category,
            jobId,
        });
    }

    /// <summary>
    /// USD/TRY, EUR/TRY ve gram altın fiyat geçmişini tazeler. full=true seriyi baştan çeker.
    /// </summary>
    [HttpPost("parity/sync")]
    [ProducesResponseType(typeof(SyncParityHistoryResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> SyncParity(
        [FromQuery] bool full = false,
        CancellationToken ct = default)
        => Ok(await _mediator.Send(new SyncParityHistoryCommand(full), ct));

    private static TimeMachineCategory? ParseCategory(string? value)
        => (value ?? "all").Trim().ToLowerInvariant() switch
        {
            "bist" => TimeMachineCategory.Bist,
            "crypto" => TimeMachineCategory.Crypto,
            "parity" => TimeMachineCategory.Parity,
            _ => null,
        };
}
