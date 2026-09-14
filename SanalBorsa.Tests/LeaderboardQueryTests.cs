using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Application.Portfolio.Queries.GetLeaderboard;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;
using SanalBorsa.Domain.Interfaces.Repositories;
using SanalBorsa.Domain.Models;

namespace SanalBorsa.Tests;

/// <summary>
/// Sıralamanın çekirdeği: portföy değeri = nakit + Σ(miktar × güncel fiyat), dolar bazlı
/// varlıklar TL'ye çevrilerek. Buradaki asıl risk, USD saklanan kripto/ABD varlıklarının
/// çevrilmemesi ya da iki kez çevrilmesi — ikisi de sıralamayı tamamen bozar.
/// </summary>
public class LeaderboardQueryTests
{
    private const decimal UsdTry = 40m;

    private static UserPortfolio Portfolio(
        string username, decimal cash, params PortfolioHolding[] holdings) => new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        Cash = cash,
        User = new User
        {
            Username = username,
            DisplayName = username,
            ShowTradeHistoryPublic = true,
        },
        Holdings = holdings.ToList(),
    };

    private static PortfolioHolding Holding(string symbol, MarketType market, decimal qty, decimal avgCost)
        => new() { Symbol = symbol, MarketType = market, Quantity = qty, AvgCost = avgCost };

    private static GetLeaderboardQueryHandler BuildHandler(
        IReadOnlyList<UserPortfolio> portfolios,
        Dictionary<(string Symbol, MarketType Market), decimal> prices)
    {
        var uow = Substitute.For<IUnitOfWork>();
        var portfolioRepo = Substitute.For<IPortfolioRepository>();
        var stockRepo = Substitute.For<IStockRepository>();
        var priceRepo = Substitute.For<IStockPriceHistoryRepository>();

        portfolioRepo.GetAllWithUserAndHoldingsAsync(Arg.Any<CancellationToken>())
            .Returns(portfolios);

        // Sembol → yapay stok kimliği; fiyat sorgusu bu kimlikler üzerinden yanıtlanır.
        var nextId = 1;
        var idBySymbol = new Dictionary<(string, MarketType), int>();
        foreach (var key in prices.Keys) idBySymbol[key] = nextId++;

        stockRepo.GetAllActiveAsync(Arg.Any<CancellationToken>(), Arg.Any<MarketType>())
            .Returns(call =>
            {
                var market = call.ArgAt<MarketType>(1);
                return idBySymbol
                    .Where(kv => kv.Key.Item2 == market)
                    .Select(kv => new Stock
                    {
                        Id = kv.Value,
                        Symbol = kv.Key.Item1,
                        MarketType = market,
                    })
                    .ToList();
            });

        priceRepo.GetMarketSnapshotsAsync(
                Arg.Any<IReadOnlyList<int>>(), Arg.Any<int>(), Arg.Any<CancellationToken>(), Arg.Any<int?>())
            .Returns(call =>
            {
                var ids = call.ArgAt<IReadOnlyList<int>>(0);
                var map = new Dictionary<int, MarketPriceSnapshot>();
                foreach (var id in ids)
                {
                    var key = idBySymbol.First(kv => kv.Value == id).Key;
                    map[id] = new MarketPriceSnapshot(prices[key], null, null, null, []);
                }
                return (IReadOnlyDictionary<int, MarketPriceSnapshot>)map;
            });

        uow.Portfolios.Returns(portfolioRepo);
        uow.Stocks.Returns(stockRepo);
        uow.PriceHistories.Returns(priceRepo);

        var fx = Substitute.For<IPortfolioFxRateProvider>();
        fx.GetUsdTryRateAsync(Arg.Any<CancellationToken>()).Returns(UsdTry);

        return new GetLeaderboardQueryHandler(
            uow, fx,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<GetLeaderboardQueryHandler>.Instance);
    }

    [Fact]
    public async Task Sadece_nakit_tutan_kullanici_baslangic_degerinde_kalir()
    {
        var handler = BuildHandler([Portfolio("nakitci", 1_000_000m)], []);

        var result = await handler.Handle(new GetLeaderboardQuery(), CancellationToken.None);

        result.Entries.Should().HaveCount(1);
        result.Entries[0].PortfolioValue.Should().Be(1_000_000m);
        result.Entries[0].GainPct.Should().Be(0m);
    }

    [Fact]
    public async Task Bist_varligi_TL_olarak_dogrudan_eklenir()
    {
        var handler = BuildHandler(
            [Portfolio("bistci", 500_000m, Holding("THYAO", MarketType.Bist, qty: 1_000m, avgCost: 400m))],
            new() { [("THYAO", MarketType.Bist)] = 500m });

        var result = await handler.Handle(new GetLeaderboardQuery(), CancellationToken.None);

        // 500.000 nakit + 1.000 × 500 ₺ = 1.000.000
        result.Entries[0].PortfolioValue.Should().Be(1_000_000m);
    }

    [Fact]
    public async Task Kripto_varligi_dolar_kuruyla_TL_ye_cevrilir()
    {
        var handler = BuildHandler(
            [Portfolio("kriptocu", 200_000m, Holding("BTCUSDT", MarketType.Crypto, qty: 2m, avgCost: 9_000m))],
            new() { [("BTCUSDT", MarketType.Crypto)] = 10_000m });

        var result = await handler.Handle(new GetLeaderboardQuery(), CancellationToken.None);

        // 200.000 + (2 × 10.000 $ × 40) = 200.000 + 800.000 = 1.000.000
        result.Entries[0].PortfolioValue.Should().Be(1_000_000m);
    }

    [Fact]
    public async Task Abd_hissesi_de_kurla_cevrilir()
    {
        var handler = BuildHandler(
            [Portfolio("amerikanci", 0m, Holding("AAPL", MarketType.UsStocks, qty: 100m, avgCost: 200m))],
            new() { [("AAPL", MarketType.UsStocks)] = 250m });

        var result = await handler.Handle(new GetLeaderboardQuery(), CancellationToken.None);

        // 100 × 250 $ × 40 = 1.000.000
        result.Entries[0].PortfolioValue.Should().Be(1_000_000m);
    }

    [Fact]
    public async Task Siralama_portfoy_degerine_gore_azalan()
    {
        var handler = BuildHandler(
        [
            Portfolio("ucuncu", 900_000m),
            Portfolio("birinci", 2_000_000m),
            Portfolio("ikinci", 1_500_000m),
        ], []);

        var result = await handler.Handle(new GetLeaderboardQuery(), CancellationToken.None);

        result.Entries.Select(e => e.Username)
            .Should().ContainInOrder("birinci", "ikinci", "ucuncu");
        result.Entries[0].Rank.Should().Be(1);
        result.Entries[2].Rank.Should().Be(3);
    }

    [Fact]
    public async Task Getiri_yuzdesi_bir_milyon_baslangica_gore_hesaplanir()
    {
        var handler = BuildHandler([Portfolio("kazanan", 1_250_000m)], []);

        var result = await handler.Handle(new GetLeaderboardQuery(), CancellationToken.None);

        result.Entries[0].GainPct.Should().Be(25m);
    }

    [Fact]
    public async Task Fiyati_bulunamayan_varlik_ortalama_maliyetiyle_degerlenir()
    {
        // Veri boşluğu yüzünden kullanıcının sıralaması sıfırlanmamalı.
        var handler = BuildHandler(
            [Portfolio("eksikveri", 0m, Holding("KAYIP", MarketType.Bist, qty: 100m, avgCost: 50m))],
            []);

        var result = await handler.Handle(new GetLeaderboardQuery(), CancellationToken.None);

        result.Entries[0].PortfolioValue.Should().Be(5_000m);
    }

    [Fact]
    public async Task Take_parametresi_listeyi_kirpar()
    {
        var handler = BuildHandler(
        [
            Portfolio("a", 3_000_000m),
            Portfolio("b", 2_000_000m),
            Portfolio("c", 1_000_000m),
        ], []);

        var result = await handler.Handle(new GetLeaderboardQuery(Take: 2), CancellationToken.None);

        result.Entries.Should().HaveCount(2);
        // Toplam katılımcı sayısı kırpmadan ÖNCEKİ değer olmalı.
        result.TotalParticipants.Should().Be(3);
    }

    [Fact]
    public async Task Portfoy_yoksa_bos_liste_doner()
    {
        var handler = BuildHandler([], []);

        var result = await handler.Handle(new GetLeaderboardQuery(), CancellationToken.None);

        result.Entries.Should().BeEmpty();
        result.TotalParticipants.Should().Be(0);
    }
}
