using FluentAssertions;
using NSubstitute;
using SanalBorsa.Application.Common;
using SanalBorsa.Application.Common.Exceptions;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Application.Portfolio.Commands.BuyStock;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;
using SanalBorsa.Domain.Interfaces.Repositories;
using SanalBorsa.Domain.Models;

namespace SanalBorsa.Tests;

/// <summary>
/// Alım akışının iş kuralları. Para söz konusu olduğu için ağırlıklı ortalama maliyet
/// hesabı ve bakiye kontrolü ayrıca doğrulanıyor — ortalama maliyet yanlış hesaplanırsa
/// kullanıcının kâr/zararı sessizce bozulur.
/// </summary>
public class BuyStockHandlerTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    /// <summary>Seansın AÇIK olduğu bir an (TR 21:00) — testler günün saatinden bağımsız olsun.</summary>
    private static readonly DateTimeOffset MarketOpen =
        new(new DateTime(2026, 3, 10, 18, 0, 0, DateTimeKind.Utc));   // 21:00 TR

    /// <summary>Seansın KAPALI olduğu bir an (TR 13:00).</summary>
    private static readonly DateTimeOffset MarketClosed =
        new(new DateTime(2026, 3, 10, 10, 0, 0, DateTimeKind.Utc));   // 13:00 TR

    private static (BuyStockCommandHandler Handler, UserPortfolio Portfolio) Build(
        decimal cash = 1_000_000m,
        decimal lastClose = 100m,
        PortfolioHolding[]? holdings = null,
        DateTimeOffset? now = null,
        string? haltReason = null)
    {
        var portfolio = new UserPortfolio
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            Cash = cash,
            Holdings = (holdings ?? []).ToList(),
            Transactions = [],
        };

        var stock = new Stock { Id = 1, Symbol = "THYAO", MarketType = MarketType.Bist, TradingHaltReason = haltReason };

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
        clock.UtcNow.Returns(now ?? MarketOpen);

        return (new BuyStockCommandHandler(uow, clock), portfolio);
    }

    [Fact]
    public async Task Basarili_alim_nakiti_dusurur_ve_pozisyon_acar()
    {
        var (handler, portfolio) = Build(cash: 1_000_000m, lastClose: 100m);

        await handler.Handle(new BuyStockCommand(UserId, "THYAO", 10), CancellationToken.None);

        portfolio.Cash.Should().Be(999_000m);           // 1.000.000 - (10 × 100)
        portfolio.Holdings.Should().ContainSingle();
        portfolio.Holdings.First().Quantity.Should().Be(10m);
        portfolio.Holdings.First().AvgCost.Should().Be(100m);
        portfolio.Transactions.Should().ContainSingle();
    }

    [Fact]
    public async Task Yetersiz_bakiye_reddedilir()
    {
        var (handler, portfolio) = Build(cash: 500m, lastClose: 100m);

        var act = () => handler.Handle(new BuyStockCommand(UserId, "THYAO", 10), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*Yetersiz bakiye*");
        portfolio.Cash.Should().Be(500m);               // dokunulmamalı
        portfolio.Holdings.Should().BeEmpty();
    }

    [Fact]
    public async Task Seans_kapaliyken_alim_yapilamaz()
    {
        var (handler, _) = Build(now: MarketClosed);

        var act = () => handler.Handle(new BuyStockCommand(UserId, "THYAO", 10), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*BIST_CLOSED*");
    }

    [Fact]
    public async Task Islem_gormeyen_hisse_alinamaz()
    {
        var (handler, _) = Build(haltReason: "tedbir kararı uygulanmaktadır");

        var act = () => handler.Handle(new BuyStockCommand(UserId, "THYAO", 10), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*tedbir*");
    }

    [Fact]
    public async Task Mevcut_pozisyona_eklemede_agirlikli_ortalama_maliyet_hesaplanir()
    {
        // Elde 10 lot × 100 ₺ varken, 10 lot daha 200 ₺'den alınırsa ortalama 150 ₺ olmalı.
        var existing = new PortfolioHolding
        {
            Symbol = "THYAO",
            MarketType = MarketType.Bist,
            Quantity = 10m,
            AvgCost = 100m,
        };
        var (handler, portfolio) = Build(cash: 1_000_000m, lastClose: 200m, holdings: [existing]);

        await handler.Handle(new BuyStockCommand(UserId, "THYAO", 10), CancellationToken.None);

        var holding = portfolio.Holdings.Single();
        holding.Quantity.Should().Be(20m);
        holding.AvgCost.Should().Be(150m);
    }

    [Fact]
    public async Task Sifir_lot_reddedilir()
    {
        var (handler, _) = Build();

        var act = () => handler.Handle(new BuyStockCommand(UserId, "THYAO", 0), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*0'dan büyük*");
    }

    [Fact]
    public async Task Islem_kaydi_dogru_alanlarla_olusur()
    {
        var (handler, portfolio) = Build(cash: 1_000_000m, lastClose: 250m);

        await handler.Handle(new BuyStockCommand(UserId, "THYAO", 4), CancellationToken.None);

        var tx = portfolio.Transactions.Single();
        tx.Symbol.Should().Be("THYAO");
        tx.Side.Should().Be(TxSide.Buy);
        tx.Quantity.Should().Be(4m);
        tx.Price.Should().Be(250m);
        tx.Total.Should().Be(1_000m);
        tx.MarketType.Should().Be(MarketType.Bist);
    }
}
