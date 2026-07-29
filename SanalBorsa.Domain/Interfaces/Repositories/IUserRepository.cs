using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Domain.Interfaces.Repositories;

public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByFirebaseUidAsync(string uid, CancellationToken ct = default);

    Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default);

    Task<bool> UsernameExistsAsync(string username, CancellationToken ct = default);

    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);

    Task<User?> GetByPhoneAsync(string phone, CancellationToken ct = default);

    Task<User?> GetWithPortfolioAsync(Guid userId, CancellationToken ct = default);
}
