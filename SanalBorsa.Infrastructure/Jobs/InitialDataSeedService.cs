using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Seeds;
using SanalBorsa.Application.Indices.Commands.BootstrapMarketIndices;
using SanalBorsa.Application.Stocks.Commands.BootstrapMarketData;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Infrastructure.Jobs;

/// <summary>
/// Runs once at startup. Triggers market bootstrap when data is missing.
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

        if (existingStocks.Count == 0 || !hasPriceData)
        {
            _logger.LogInformation(
                "Stock bootstrap required — Stocks: {StockCount}, HasPriceData: {HasPriceData}",
                existingStocks.Count, hasPriceData);

            await mediator.Send(new BootstrapMarketDataCommand(), stoppingToken);
        }
        else
        {
            _logger.LogInformation(
                "Database already has {StockCount} stocks and price history — skipping stock bootstrap",
                existingStocks.Count);
        }

        var indexSymbols = MarketInstrumentSeed.All.Select(e => e.Symbol).ToList();
        var indexStocks = await uow.Stocks.GetBySymbolsAsync(indexSymbols, stoppingToken);
        var indicesNeedBootstrap = indexStocks.Count < indexSymbols.Count
            || indexStocks.Any(s => s.EarliestDataDate is null || s.NeedsHistoryRefresh);

        if (indicesNeedBootstrap)
        {
            _logger.LogInformation("Market instruments bootstrap required — starting index/FX history fetch...");
            await mediator.Send(new BootstrapMarketIndicesCommand(), stoppingToken);
        }

        _logger.LogInformation("Startup market bootstrap completed");
    }
}
