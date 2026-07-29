using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SanalBorsa.Application.Auth.Commands.CompleteRegistration;
using SanalBorsa.Application.Auth.Commands.LoginWithFirebase;
using SanalBorsa.Application.Auth.Commands.LoginWithPassword;
using SanalBorsa.Application.Auth.Commands.RefreshToken;
using SanalBorsa.Application.Auth.Commands.RegisterWithPassword;
using SanalBorsa.Application.Auth.Commands.UpdatePrivacySettings;
using SanalBorsa.Application.Auth.Queries.CheckUsernameAvailable;
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
    /// Firebase ID Token ile giriş.
    /// Yeni kullanıcıda NeedsProfile=true + profil ipuçları döner; kayıt CompleteRegistration ile biter.
    /// </summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthExchangeResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new LoginWithFirebaseCommand(request.IdToken), ct);
        return Ok(result);
    }

    /// <summary>Kullanıcı adı + şifre ile giriş.</summary>
    [HttpPost("password/login")]
    [ProducesResponseType(typeof(LoginResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> PasswordLogin([FromBody] PasswordLoginRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new LoginWithPasswordCommand(request.Username, request.Password),
            ct);
        return Ok(result);
    }

    /// <summary>
    /// İlk kayıt (Firebase): kullanıcı adı (zorunlu) + görünen ad (opsiyonel).
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(LoginResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new CompleteRegistrationCommand(request.IdToken, request.Username, request.DisplayName),
            ct);
        return Ok(result);
    }

    /// <summary>Form ile kayıt: kullanıcı adı + şifre (+ opsiyonel görünen ad).</summary>
    [HttpPost("password/register")]
    [ProducesResponseType(typeof(LoginResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PasswordRegister([FromBody] PasswordRegisterRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new RegisterWithPasswordCommand(
                request.Username,
                request.Password,
                request.PasswordConfirm,
                request.DisplayName),
            ct);
        return Ok(result);
    }

    /// <summary>Kullanıcı adı müsait mi?</summary>
    [HttpGet("username-available")]
    [ProducesResponseType(typeof(UsernameAvailabilityDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UsernameAvailable([FromQuery] string username, CancellationToken ct)
    {
        var result = await _mediator.Send(new CheckUsernameAvailableQuery(username), ct);
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

    /// <summary>Gizlilik ayarları — işlem geçmişinin herkese açık olup olmadığı.</summary>
    [HttpPatch("privacy")]
    [Authorize]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdatePrivacy(
        [FromBody] PrivacySettingsRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new UpdatePrivacySettingsCommand(GetUserId(), request.ShowTradeHistoryPublic),
            ct);
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
public record RegisterRequest(string IdToken, string Username, string? DisplayName = null);
public record PasswordLoginRequest(string Username, string Password);
public record PasswordRegisterRequest(
    string Username,
    string Password,
    string PasswordConfirm,
    string? DisplayName = null);
public record RefreshRequest(string RefreshToken);
public record PrivacySettingsRequest(bool ShowTradeHistoryPublic);
