using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SanalBorsa.Application.Notifications.Commands.MarkNotificationsRead;
using SanalBorsa.Application.Notifications.Queries.GetNotifications;

namespace SanalBorsa.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
public class NotificationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public NotificationsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Kullanıcının son bildirimleri + okunmamış sayısı (zil ikonu rozeti için).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(GetNotificationsResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetNotificationsQuery(GetUserId()), ct);
        return Ok(result);
    }

    /// <summary>Tüm bildirimleri okundu işaretler — bildirim paneli açılınca çağrılır.</summary>
    [HttpPost("mark-read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkRead(CancellationToken ct)
    {
        await _mediator.Send(new MarkNotificationsReadCommand(GetUserId()), ct);
        return NoContent();
    }

    private Guid GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue("sub")
               ?? throw new UnauthorizedAccessException("Token'da kullanıcı ID'si bulunamadı.");
        return Guid.Parse(sub);
    }
}
