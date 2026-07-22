using MediatR;
using SanalBorsa.Application.Common.Interfaces;

namespace SanalBorsa.Application.Auth.Commands.LoginWithFirebase;

/// <summary>
/// Frontend'den gelen Firebase ID Token ile giriş / otomatik kayıt.
/// Google ve Telefon OTP akışları bu tek komuttan geçer.
/// </summary>
public record LoginWithFirebaseCommand(string IdToken) : IRequest<LoginResult>;

public record LoginResult(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    UserDto User);

public record UserDto(
    Guid   Id,
    string DisplayName,
    string? Email,
    string? PhoneNumber,
    string? AvatarUrl,
    string Provider,
    decimal PortfolioCashTry,
    decimal PortfolioCashUsd);
