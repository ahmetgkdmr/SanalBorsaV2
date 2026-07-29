using Microsoft.EntityFrameworkCore;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces.Repositories;
using SanalBorsa.Infrastructure.Data;

namespace SanalBorsa.Infrastructure.Repositories;

public class UserRepository : BaseRepository<User>, IUserRepository
{
    public UserRepository(AppDbContext context) : base(context) { }

    public async Task<User?> GetByFirebaseUidAsync(string uid, CancellationToken ct = default)
        => await DbSet.FirstOrDefaultAsync(u => u.FirebaseUid == uid, ct);

    public async Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default)
    {
        var normalized = username.Trim().ToLowerInvariant();
        return await DbSet.FirstOrDefaultAsync(u => u.Username.ToLower() == normalized, ct);
    }

    public async Task<bool> UsernameExistsAsync(string username, CancellationToken ct = default)
    {
        var normalized = username.Trim().ToLowerInvariant();
        return await DbSet.AnyAsync(u => u.Username.ToLower() == normalized, ct);
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
        => await DbSet.FirstOrDefaultAsync(u => u.Email == email, ct);

    public async Task<User?> GetByPhoneAsync(string phone, CancellationToken ct = default)
        => await DbSet.FirstOrDefaultAsync(u => u.PhoneNumber == phone, ct);

    public async Task<User?> GetWithPortfolioAsync(Guid userId, CancellationToken ct = default)
        => await DbSet
            .Include(u => u.Portfolio)
                .ThenInclude(p => p!.Holdings)
            .Include(u => u.Portfolio)
                .ThenInclude(p => p!.Transactions)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);
}
