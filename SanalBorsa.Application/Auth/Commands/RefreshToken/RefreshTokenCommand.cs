using MediatR;
using SanalBorsa.Application.Auth.Commands.LoginWithFirebase;

namespace SanalBorsa.Application.Auth.Commands.RefreshToken;

public record RefreshTokenCommand(string RefreshToken) : IRequest<LoginResult>;
