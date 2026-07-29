using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Infrastructure.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.FirebaseUid).HasMaxLength(128);
        builder.HasIndex(u => u.FirebaseUid)
            .IsUnique()
            .HasFilter("[FirebaseUid] IS NOT NULL");

        builder.Property(u => u.Username).HasMaxLength(32).IsRequired();
        builder.HasIndex(u => u.Username).IsUnique();

        builder.Property(u => u.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.Email).HasMaxLength(256);
        builder.Property(u => u.PhoneNumber).HasMaxLength(20);
        builder.Property(u => u.AvatarUrl).HasMaxLength(500);
        builder.Property(u => u.PasswordHash).HasMaxLength(500);
        builder.Property(u => u.ShowTradeHistoryPublic).HasDefaultValue(true);

        builder.HasIndex(u => u.Email).IsUnique().HasFilter("[Email] IS NOT NULL");
        builder.HasIndex(u => u.PhoneNumber).IsUnique().HasFilter("[PhoneNumber] IS NOT NULL");

        builder.HasOne(u => u.Portfolio)
               .WithOne(p => p.User)
               .HasForeignKey<UserPortfolio>(p => p.UserId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public class UserPortfolioConfiguration : IEntityTypeConfiguration<UserPortfolio>
{
    public void Configure(EntityTypeBuilder<UserPortfolio> builder)
    {
        builder.ToTable("UserPortfolios");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Cash).HasPrecision(18, 4);
        builder.Property(p => p.CashUsd).HasPrecision(18, 4);
        builder.HasIndex(p => p.UserId).IsUnique();

        builder.HasMany(p => p.Holdings)
               .WithOne(h => h.Portfolio)
               .HasForeignKey(h => h.PortfolioId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Transactions)
               .WithOne(t => t.Portfolio)
               .HasForeignKey(t => t.PortfolioId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public class PortfolioHoldingConfiguration : IEntityTypeConfiguration<PortfolioHolding>
{
    public void Configure(EntityTypeBuilder<PortfolioHolding> builder)
    {
        builder.ToTable("PortfolioHoldings");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Symbol).HasMaxLength(32).IsRequired();
        builder.Property(h => h.Quantity).HasPrecision(18, 8);
        builder.Property(h => h.AvgCost).HasPrecision(18, 8);
        builder.HasIndex(h => new { h.PortfolioId, h.Symbol, h.MarketType }).IsUnique();
    }
}

public class PortfolioTransactionConfiguration : IEntityTypeConfiguration<PortfolioTransaction>
{
    public void Configure(EntityTypeBuilder<PortfolioTransaction> builder)
    {
        builder.ToTable("PortfolioTransactions");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Symbol).HasMaxLength(32).IsRequired();
        builder.Property(t => t.Quantity).HasPrecision(18, 8);
        builder.Property(t => t.Price).HasPrecision(18, 8);
        builder.Property(t => t.Total).HasPrecision(18, 8);
        builder.Property(t => t.FillBreakdownJson).HasColumnType("nvarchar(max)");
    }
}
