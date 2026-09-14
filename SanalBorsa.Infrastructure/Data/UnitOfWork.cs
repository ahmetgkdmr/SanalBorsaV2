using Microsoft.EntityFrameworkCore;
using SanalBorsa.Application.Common.Exceptions;
using SanalBorsa.Domain.Interfaces;
using SanalBorsa.Domain.Interfaces.Repositories;
using SanalBorsa.Infrastructure.Repositories;

namespace SanalBorsa.Infrastructure.Data;

public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;

    private IStockRepository?              _stocks;
    private IStockPriceHistoryRepository?  _priceHistories;
    private IStockIntradayBarRepository?   _intradayBars;
    private ICorporateActionRepository?    _corporateActions;
    private IUserRepository?               _users;
    private IPortfolioRepository?          _portfolios;
    private ITopGainerRepository?          _topGainers;
    private ITimeMachineLeaderRepository?  _timeMachineLeaders;
    private INotificationRepository?       _notifications;
    private IRefreshTokenRepository?       _refreshTokens;

    public UnitOfWork(AppDbContext context)
    {
        _context = context;
    }

    public IStockRepository             Stocks             => _stocks             ??= new StockRepository(_context);
    public IStockPriceHistoryRepository PriceHistories     => _priceHistories     ??= new StockPriceHistoryRepository(_context);
    public IStockIntradayBarRepository  IntradayBars       => _intradayBars       ??= new StockIntradayBarRepository(_context);
    public ICorporateActionRepository   CorporateActions   => _corporateActions   ??= new CorporateActionRepository(_context);
    public IUserRepository              Users              => _users              ??= new UserRepository(_context);
    public IPortfolioRepository         Portfolios         => _portfolios         ??= new PortfolioRepository(_context);
    public ITopGainerRepository         TopGainers         => _topGainers         ??= new TopGainerRepository(_context);
    public ITimeMachineLeaderRepository TimeMachineLeaders => _timeMachineLeaders ??= new TimeMachineLeaderRepository(_context);
    public INotificationRepository      Notifications      => _notifications      ??= new NotificationRepository(_context);
    public IRefreshTokenRepository      RefreshTokens      => _refreshTokens      ??= new RefreshTokenRepository(_context);

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            return await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(ex);
        }
    }

    public void ClearChanges()
        => _context.ChangeTracker.Clear();

    public void Dispose()
        => _context.Dispose();
}
