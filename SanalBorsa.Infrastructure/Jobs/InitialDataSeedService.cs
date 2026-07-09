using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Stocks.Commands.BootstrapMarketData;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Infrastructure.Jobs;

/// <summary>
/// Runs once at startup. Triggers full market bootstrap when Stocks or StockPriceHistories are empty.
/// </summary>
public class InitialDataSeedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InitialDataSeedService> _logger;

    public InitialDataSeedService(IServiceScopeFactory scopeFactory, ILogger<InitialDataSeedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var existingStocks = await uow.Stocks.GetAllAsync(stoppingToken);
        var hasPriceData = await uow.PriceHistories.AnyAsync(stoppingToken);

        if (existingStocks.Count > 0 && hasPriceData)
        {
            _logger.LogInformation(
                "Database already has {StockCount} stocks and price history — skipping bootstrap",
                existingStocks.Count);
            return;
        }

        _logger.LogInformation(
            "Bootstrap required — Stocks: {StockCount}, HasPriceData: {HasPriceData}. Starting full market bootstrap...",
            existingStocks.Count, hasPriceData);

        await mediator.Send(new BootstrapMarketDataCommand(), stoppingToken);

        _logger.LogInformation("Startup market bootstrap completed");
    }
}
