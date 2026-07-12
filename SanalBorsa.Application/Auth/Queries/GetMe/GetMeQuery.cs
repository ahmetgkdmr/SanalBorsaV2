using MediatR;
using SanalBorsa.Application.Auth.Commands.LoginWithFirebase;

namespace SanalBorsa.Application.Auth.Queries.GetMe;

public record GetMeQuery(Guid UserId) : IRequest<UserDto>;
