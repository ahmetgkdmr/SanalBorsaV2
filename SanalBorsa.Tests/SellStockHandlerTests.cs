using FluentAssertions;
using NSubstitute;
using SanalBorsa.Application.Common.Exceptions;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Application.Portfolio.Commands.SellStock;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;
using SanalBorsa.Domain.Interfaces.Repositories;
using SanalBorsa.Domain.Models;

namespace SanalBorsa.Tests;

/// <summary>
/// Satış akışı. Asıl risk "elde olandan fazlasını satmak" — sanal da olsa bakiyeyi
/// yoktan var edeceği için mutlaka engellenmeli.
/// </summary>
public class SellStockHandlerTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset MarketOpen =
        new(new DateTime(2026, 3, 10, 18, 0, 0, DateTimeKind.Utc));   // 21:00 TR

    private static (SellStockCommandHandler Handler, UserPortfolio Portfolio) Build(
        decimal ownedQty,
        decimal cash = 0m,
        decimal lastClose = 100m)
    {
        var holdings = ownedQty > 0
            ? new List<PortfolioHolding>
            {
                new() { Symbol = "THYAO", MarketType = MarketType.Bist, Quantity = ownedQty, AvgCost = 80m },
            }
            : [];

        var portfolio = new UserPortfolio
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            Cash = cash,
            Holdings = holdings,
            Transactions = [],
        };

        var stock = new Stock { Id = 1, Symbol = "THYAO", MarketType = MarketType.Bist };

        var uow = Substitute.For<IUnitOfWork>();
        var portfolioRepo = Substitute.For<IPortfolioRepository>();
        var stockRepo = Substitute.For<IStockRepository>();
        var priceRepo = Substitute.For<IStockPriceHistoryRepository>();

        portfolioRepo.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(portfolio);
        stockRepo.GetBySymbolAsync("THYAO", Arg.Any<CancellationToken>(), Arg.Any<MarketType>()).Returns(stock);
        priceRepo.GetMarketSnapshotsAsync(
                Arg.Any<IReadOnlyList<int>>(), Arg.Any<int>(), Arg.Any<CancellationToken>(), Arg.Any<int?>())
            .Returns((IReadOnlyDictionary<int, MarketPriceSnapshot>)new Dictionary<int, MarketPriceSnapshot>
            {
                [1] = new(lastClose, null, null, null, []),
            });

        uow.Portfolios.Returns(portfolioRepo);
        uow.Stocks.Returns(stockRepo);
        uow.PriceHistories.Returns(priceRepo);
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(MarketOpen);

        return (new SellStockCommandHandler(uow, clock), portfolio);
    }

    [Fact]
    public async Task Basarili_satis_nakiti_artirir_ve_pozisyonu_azaltir()
    {
        var (handler, portfolio) = Build(ownedQty: 100m, cash: 0m, lastClose: 150m);

        await handler.Handle(new SellStockCommand(UserId, "THYAO", 40), CancellationToken.None);

        portfolio.Cash.Should().Be(6_000m);                     // 40 × 150
        portfolio.Holdings.Single().Quantity.Should().Be(60m);
    }

    [Fact]
    public async Task Tamami_satilinca_pozisyon_kapanir()
    {
        var (handler, portfolio) = Build(ownedQty: 50m, lastClose: 100m);

        await handler.Handle(new SellStockCommand(UserId, "THYAO", 50), CancellationToken.None);

        portfolio.Holdings.Should().BeEmpty();
        portfolio.Cash.Should().Be(5_000m);
    }

    [Fact]
    public async Task Elde_olandan_fazlasi_satilamaz()
    {
        var (handler, portfolio) = Build(ownedQty: 10m, cash: 0m);

        var act = () => handler.Handle(new SellStockCommand(UserId, "THYAO", 11), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*Yeterli lot yok*");
        portfolio.Cash.Should().Be(0m);
        portfolio.Holdings.Single().Quantity.Should().Be(10m);
    }

    [Fact]
    public async Task Sahip_olunmayan_hisse_satilamaz()
    {
        var (handler, _) = Build(ownedQty: 0m);

        var act = () => handler.Handle(new SellStockCommand(UserId, "THYAO", 1), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*bulunamadı*");
    }

    [Fact]
    public async Task Satis_islem_kaydi_olusturur()
    {
        var (handler, portfolio) = Build(ownedQty: 100m, lastClose: 120m);

        await handler.Handle(new SellStockCommand(UserId, "THYAO", 25), CancellationToken.None);

        var tx = portfolio.Transactions.Single();
        tx.Side.Should().Be(TxSide.Sell);
        tx.Quantity.Should().Be(25m);
        tx.Price.Should().Be(120m);
        tx.Total.Should().Be(3_000m);
    }
}
