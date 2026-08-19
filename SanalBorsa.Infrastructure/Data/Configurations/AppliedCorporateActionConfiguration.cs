using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Infrastructure.Data.Configurations;

public class AppliedCorporateActionConfiguration : IEntityTypeConfiguration<AppliedCorporateAction>
{
    public void Configure(EntityTypeBuilder<AppliedCorporateAction> builder)
    {
        builder.ToTable("AppliedCorporateActions");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Effect)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(a => a.AppliedAt).IsRequired();

        // Aynı (olay, portföy) çifti asla iki kere işlenemez — idempotency'nin garantisi burada.
        builder.HasIndex(a => new { a.CorporateActionId, a.PortfolioId })
            .IsUnique()
            .HasDatabaseName("IX_AppliedCorporateActions_Action_Portfolio");
    }
}
