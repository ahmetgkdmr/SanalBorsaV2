using Microsoft.Extensions.Logging;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Infrastructure.Jobs;

/// <summary>
/// Süresi geçmiş yenileme token'ı kayıtlarını siler. Her giriş ve her yenileme bir satır
/// eklediği için tablo aksi hâlde sınırsız büyürdü. Süresi dolmuş bir token zaten
/// doğrulamayı geçemez; kaydını tutmanın bir faydası yok.
/// </summary>
public class ExpiredRefreshTokenCleanupJob
{
    /// <summary>Son kullanma tarihinden sonra kayıt bu kadar süre daha tutulur — bir olay
    /// incelemesi gerekirse iz kalsın diye.</summary>
    private static readonly TimeSpan RetentionAfterExpiry = TimeSpan.FromDays(7);

    private readonly IUnitOfWork _uow;
    private readonly ILogger<ExpiredRefreshTokenCleanupJob> _logger;

    public ExpiredRefreshTokenCleanupJob(IUnitOfWork uow, ILogger<ExpiredRefreshTokenCleanupJob> logger)
    {
        _uow = uow;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - RetentionAfterExpiry;
        var deleted = await _uow.RefreshTokens.DeleteExpiredAsync(cutoff, ct);

        if (deleted > 0)
            _logger.LogInformation("{Count} süresi geçmiş yenileme token'ı silindi.", deleted);
    }
}
