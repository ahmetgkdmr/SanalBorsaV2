using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SanalBorsa.Application.Auth.Commands.LoginWithFirebase;
using SanalBorsa.Application.Auth.Commands.RefreshToken;
using SanalBorsa.Application.Auth.Queries.GetMe;

namespace SanalBorsa.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;

    public AuthController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Firebase ID Token ile giriş / otomatik kayıt.
    /// Google Sign-In ve Phone OTP sonrasında frontend buraya gönderir.
    /// </summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new LoginWithFirebaseCommand(request.IdToken), ct);
        return Ok(result);
    }

    /// <summary>Access token'ı yenile.</summary>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(LoginResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new RefreshTokenCommand(request.RefreshToken), ct);
        return Ok(result);
    }

    /// <summary>Giriş yapmış kullanıcının bilgilerini döner.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _mediator.Send(new GetMeQuery(userId), ct);
        return Ok(result);
    }

    private Guid GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue("sub")
               ?? throw new UnauthorizedAccessException("Token'da kullanıcı ID'si bulunamadı.");
        return Guid.Parse(sub);
    }
}

public record LoginRequest(string IdToken);
public record RefreshRequest(string RefreshToken);
