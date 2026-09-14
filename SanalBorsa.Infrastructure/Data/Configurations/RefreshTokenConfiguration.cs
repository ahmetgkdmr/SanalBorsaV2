using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Infrastructure.Data.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");

        builder.HasKey(t => t.Id);

        // Her yenileme isteği bu alandan tek kayıt arar — benzersiz indeks hem hızlandırır
        // hem de aynı jti'nin iki kez yazılmasını engeller.
        builder.HasIndex(t => t.Jti).IsUnique();

        // Çıkışta "kullanıcının tüm aktif token'larını iptal et" sorgusu için.
        builder.HasIndex(t => new { t.UserId, t.RevokedAt });

        // Temizlik job'ı süresi geçmişleri bu indeksle tarar.
        builder.HasIndex(t => t.ExpiresAt);

        builder.Property(t => t.Jti).IsRequired();
        builder.Property(t => t.ExpiresAt).IsRequired();
        builder.Property(t => t.CreatedAt).IsRequired();

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
