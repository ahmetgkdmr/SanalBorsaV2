using Hangfire;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Application.Indices.Commands.SyncParityHistory;
using SanalBorsa.Application.Stocks.Commands.ComputeTimeMachineLeaders;
using SanalBorsa.Application.Stocks.Queries.GetTimeMachineDailyReport;
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

    /// <summary>
    /// "O gün ne alsaydım zengin olurdum?" — sadece tarih vererek BIST/Kripto/ABD için
    /// en çok kazandıran 3 ve en çok kaybettiren 3 enstrüman. Tarih işlem günü değilse
    /// önceki en yakın işlem gününe kayar.
    /// </summary>
    [HttpGet("daily-report")]
    [ProducesResponseType(typeof(TimeMachineDailyReportDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDailyReport([FromQuery] DateTime date, CancellationToken ct)
        => Ok(await _mediator.Send(new GetTimeMachineDailyReportQuery(date), ct));

    /// <summary>Liderlik tablosunun kategori bazlı özeti (satır sayısı, tarih aralığı).</summary>
    [HttpGet("leaders/stats")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStats([FromServices] IUnitOfWork uow, CancellationToken ct)
        => Ok(await uow.TimeMachineLeaders.GetStatsAsync(ct));

    /// <summary>
    /// Liderlik tablosunu yeniden hesaplar (manuel tetik). category=all|bist|crypto|parity.
    /// Varsayılan: arka planda (Hangfire — production worker'ı işler) çalışır.
    /// sync=true: bu isteği alan sürecin kendisinde, hemen ve senkron çalışır — local'de
    /// (Hangfire worker olmadan) yeni kodu production'a deploy etmeden test etmek için.
    /// </summary>
    [HttpPost("leaders/compute")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ComputeTimeMachineLeadersResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Compute(
        [FromQuery] string category = "all",
        [FromQuery] bool sync = false,
        CancellationToken ct = default)
    {
        var parsed = ParseCategory(category);

        if (sync)
        {
            var result = await _mediator.Send(new ComputeTimeMachineLeadersCommand(parsed), ct);
            return Ok(result);
        }

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
            "us" or "usstocks" => TimeMachineCategory.UsStocks,
            "parity" => TimeMachineCategory.Parity,
            _ => null,
        };
}
