using Microsoft.EntityFrameworkCore;
using SanalBorsa.Domain.Entities;
using System.Reflection;

namespace SanalBorsa.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Stock> Stocks => Set<Stock>();

    public DbSet<StockPriceHistory> StockPriceHistories => Set<StockPriceHistory>();

    public DbSet<CorporateAction> CorporateActions => Set<CorporateAction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        base.OnModelCreating(modelBuilder);
    }
}
