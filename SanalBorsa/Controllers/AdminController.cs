using MediatR;
using Microsoft.AspNetCore.Mvc;
using SanalBorsa.Application.Admin.Commands;

namespace SanalBorsa.API.Controllers;

/// <summary>
/// TEK SEFERLİK bakım endpoint'i — proje canlıya açılmadan önce temiz bir taban için.
/// İş bitince bu controller (ve WipeAllUsersCommand) tamamen silinebilir.
/// </summary>
[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IConfiguration _config;

    public AdminController(IMediator mediator, IConfiguration config)
    {
        _mediator = mediator;
        _config = config;
    }

    /// <summary>DB'deki ve Firebase'deki TÜM kullanıcıları siler. Geri alınamaz.</summary>
    [HttpPost("wipe-users")]
    public async Task<IActionResult> WipeUsers([FromHeader(Name = "X-Admin-Secret")] string? secret, CancellationToken ct)
    {
        var expected = _config["Admin:WipeSecret"];
        if (string.IsNullOrEmpty(expected) || secret != expected)
            return Unauthorized(new { message = "Geçersiz veya eksik X-Admin-Secret." });

        var result = await _mediator.Send(new WipeAllUsersCommand(), ct);
        return Ok(result);
    }
}
