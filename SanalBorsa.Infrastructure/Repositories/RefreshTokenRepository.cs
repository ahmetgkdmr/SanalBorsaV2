using Microsoft.EntityFrameworkCore;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces.Repositories;
using SanalBorsa.Infrastructure.Data;

namespace SanalBorsa.Infrastructure.Repositories;

public class RefreshTokenRepository : BaseRepository<RefreshToken>, IRefreshTokenRepository
{
    public RefreshTokenRepository(AppDbContext context) : base(context) { }

    /// <summary>
    /// Takip AÇIK bırakılıyor (AsNoTracking yok): çağıran taraf bulduğu kaydı rotasyonda
    /// iptal etmek için hemen güncelliyor.
    /// </summary>
    public async Task<RefreshToken?> GetByJtiAsync(Guid jti, CancellationToken ct = default)
        => await DbSet.FirstOrDefaultAsync(t => t.Jti == jti, ct);

    public async Task<int> RevokeAllForUserAsync(Guid userId, DateTime revokedAt, CancellationToken ct = default)
        => await DbSet
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, revokedAt), ct);

    public async Task<int> DeleteExpiredAsync(DateTime olderThanUtc, CancellationToken ct = default)
        => await DbSet
            .Where(t => t.ExpiresAt < olderThanUtc)
            .ExecuteDeleteAsync(ct);
}
