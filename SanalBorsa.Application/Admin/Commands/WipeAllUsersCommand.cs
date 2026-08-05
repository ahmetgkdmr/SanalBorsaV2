using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Admin.Commands;

/// <summary>
/// TEK SEFERLİK bakım komutu: DB'deki ve Firebase'deki TÜM kullanıcıları siler.
/// Geri alınamaz. Sadece proje henüz canlıya açılmadan önce temiz bir taban için kullanılır.
/// </summary>
public record WipeAllUsersCommand : IRequest<WipeAllUsersResult>;

public record WipeAllUsersResult(int DbUsersDeleted, int FirebaseUsersDeleted);

public class WipeAllUsersCommandHandler : IRequestHandler<WipeAllUsersCommand, WipeAllUsersResult>
{
    private readonly IUnitOfWork _uow;
    private readonly IFirebaseAuthProvider _firebase;
    private readonly ILogger<WipeAllUsersCommandHandler> _logger;

    public WipeAllUsersCommandHandler(
        IUnitOfWork uow,
        IFirebaseAuthProvider firebase,
        ILogger<WipeAllUsersCommandHandler> logger)
    {
        _uow = uow;
        _firebase = firebase;
        _logger = logger;
    }

    public async Task<WipeAllUsersResult> Handle(WipeAllUsersCommand request, CancellationToken cancellationToken)
    {
        var users = await _uow.Users.GetAllAsync(cancellationToken);
        _uow.Users.RemoveRange(users);
        await _uow.SaveChangesAsync(cancellationToken);
        _logger.LogWarning("WipeAllUsers: {Count} kullanıcı DB'den silindi.", users.Count);

        var firebaseDeleted = await _firebase.DeleteAllUsersAsync(cancellationToken);

        return new WipeAllUsersResult(users.Count, firebaseDeleted);
    }
}
