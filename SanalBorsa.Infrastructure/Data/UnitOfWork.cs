using SanalBorsa.Domain.Interfaces;
using SanalBorsa.Domain.Interfaces.Repositories;
using SanalBorsa.Infrastructure.Repositories;

namespace SanalBorsa.Infrastructure.Data;

public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;
    private IStockRepository? _stocks;
    private IStockPriceHistoryRepository? _priceHistories;
    private ICorporateActionRepository? _corporateActions;

    public UnitOfWork(AppDbContext context)
    {
        _context = context;
    }

    public IStockRepository Stocks
        => _stocks ??= new StockRepository(_context);

    public IStockPriceHistoryRepository PriceHistories
        => _priceHistories ??= new StockPriceHistoryRepository(_context);

    public ICorporateActionRepository CorporateActions
        => _corporateActions ??= new CorporateActionRepository(_context);

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);

    public void Dispose()
        => _context.Dispose();
}
