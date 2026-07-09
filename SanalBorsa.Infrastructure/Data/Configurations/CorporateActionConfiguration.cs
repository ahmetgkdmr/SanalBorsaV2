using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Infrastructure.Data.Configurations;

public class CorporateActionConfiguration : IEntityTypeConfiguration<CorporateAction>
{
    public void Configure(EntityTypeBuilder<CorporateAction> builder)
    {
        builder.ToTable("CorporateActions");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.ActionType)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(a => a.ActionDate)
            .IsRequired()
            .HasColumnType("date");

        builder.Property(a => a.Value)
            .IsRequired()
            .HasPrecision(18, 6);

        builder.Property(a => a.Description)
            .HasMaxLength(500);

        builder.Property(a => a.CreatedAt)
            .IsRequired();

        // Prevents duplicate records for the same event
        builder.HasIndex(a => new { a.StockId, a.ActionDate, a.ActionType })
            .IsUnique()
            .HasDatabaseName("IX_CorporateActions_StockId_Date_Type");
    }
}
