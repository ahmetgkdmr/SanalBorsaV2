using System.Globalization;
using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Enums;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Portfolio.Commands.ApplyCorporateActionsToPortfolios;

/// <summary>
/// Gerçek kullanıcı portföylerine kurumsal olayları uygular — proje sohbeti (bedelli/bedelsiz/
/// temettü portföy yönetimi tasarımı):
/// - Hak sahipliği: "şu an kaç adet var" değil, "ActionDate gecesi bizim seansımız kapanana kadar
///   (ActionDate'in kendi günü saat 09:30 TR — bkz. BistTradingHours, EligibilityCutoffUtc) kaç
///   adet vardı" — PortfolioTransaction geçmişinden o ana kadarki net miktar (Buy-Sell) yeniden
///   hesaplanır. Takvim günü değil, SEANS PENCEREMİZ esas alınır: BIST sanal seansımız 19:00–09:30
///   TR açık ve o pencere boyunca hep ÖNCEKİ günün (olayı henüz yansıtmayan) kapanış fiyatıyla
///   işlem görüyor — yani ActionDate'in gece yarısından 09:30'a kadar yapılan alımlar da hâlâ eski
///   fiyatladır ve hak sahibidir. 09:30'dan (seansımız kapandıktan) sonra zaten hiç işlem olmaz.
/// - Bedelsiz: Quantity artar (bedava pay), AvgCost toplam maliyet aynı kalacak şekilde düşer.
/// - Temettü: Cash += hakEdilenAdet × TL/hisse.
/// - Bedelli: "hakkını satmış gibi" — pay/maliyet DEĞİŞMEZ, hakkın TERP'ten türetilen teorik
///   değeri kadar nakit yatırılır (gerçek "R" kodlu hak kuponu piyasa fiyatını takip etmiyoruz,
///   böyle bir enstrüman sistemimizde yok — TERP, TradingView/endeks sağlayıcılarının da kullandığı
///   standart yöntem, bu oturumda AFYON'un 2015 bedellisiyle doğrulandı).
///     TERP = (EskiFiyat + oran × Bedel) / (1 + oran); HakDeğeri = EskiFiyat − TERP
///   EskiFiyat: ActionDate'in BİR ÖNCESİNDEKİ son ham kapanış (bizim kendi fiyat verimiz, KAP'tan
///   değil — her zaman güvenilir, bkz. proje sohbeti).
/// - Idempotency: AppliedCorporateAction tablosu her (olay, portföy) çiftini kalıcı işaretler; iş
///   yarıda kesilse bile bir sonraki çalıştırma sadece KALAN portföyleri işler, tekrar etmez.
/// </summary>
public class ApplyCorporateActionsToPortfoliosCommandHandler
    : IRequestHandler<ApplyCorporateActionsToPortfoliosCommand, ApplyCorporateActionsToPortfoliosResult>
{
    private readonly IUnitOfWork _uow;
    private readonly ILogger<ApplyCorporateActionsToPortfoliosCommandHandler> _logger;

    public ApplyCorporateActionsToPortfoliosCommandHandler(
        IUnitOfWork uow,
        ILogger<ApplyCorporateActionsToPortfoliosCommandHandler> logger)
    {
        _uow = uow;
        _logger = logger;
    }

    public async Task<ApplyCorporateActionsToPortfoliosResult> Handle(
        ApplyCorporateActionsToPortfoliosCommand request,
        CancellationToken ct)
    {
        var actions = await _uow.CorporateActions.GetUnappliedPortfolioActionsAsync(DateTime.UtcNow, ct);

        int applications = 0, skipped = 0, failed = 0;

        foreach (var action in actions)
        {
            var symbol = action.Stock.Symbol;
            var market = action.Stock.MarketType;

            var portfolioIds = await _uow.Portfolios.GetPortfolioIdsWithSymbolHistoryAsync(symbol, market, ct);

            // EskiFiyat sadece Bedelli için lazım, ama tek seferde çekip döngü boyunca tekrar
            // sorgu atmayalım.
            decimal? cumPrice = null;
            if (action.ActionType == CorporateActionType.RightsIssue)
            {
                var closes = await _uow.PriceHistories.GetClosesOnOrBeforeAsync(
                    [action.StockId], action.ActionDate.AddDays(-1), ct);
                if (closes.TryGetValue(action.StockId, out var snap) && snap.Close > 0m)
                    cumPrice = snap.Close;
            }

            var allSucceeded = true;

            foreach (var portfolioId in portfolioIds)
            {
                if (await _uow.CorporateActions.IsAppliedToPortfolioAsync(action.Id, portfolioId, ct))
                    continue; // önceki (yarıda kalmış) çalıştırmada zaten işlendi

                try
                {
                    var result = await ConcurrencySafe.RunAsync(
                        _uow, () => ApplyToPortfolioAsync(portfolioId, symbol, market, action, cumPrice, ct));

                    if (result is null)
                    {
                        skipped++;
                        continue;
                    }

                    await _uow.CorporateActions.RecordAppliedAsync(action.Id, portfolioId, result, ct);
                    await _uow.SaveChangesAsync(ct);
                    applications++;
                }
                catch (Exception ex)
                {
                    failed++;
                    allSucceeded = false;
                    _uow.ClearChanges();
                    _logger.LogError(
                        ex, "Kurumsal olay portföye uygulanamadı: actionId={ActionId} portfolioId={PortfolioId}",
                        action.Id, portfolioId);
                }
            }

            if (allSucceeded)
            {
                action.AppliedToPortfolios = true;
                _uow.CorporateActions.Update(action);
                await _uow.SaveChangesAsync(ct);
            }
        }

        _logger.LogInformation(
            "Kurumsal olay → portföy uygulaması bitti: {Actions} olay tarandı, {Applications} portföye uygulandı, " +
            "{Skipped} hak sahipliği yok, {Failed} hata.",
            actions.Count, applications, skipped, failed);

        return new ApplyCorporateActionsToPortfoliosResult(actions.Count, applications, skipped, failed);
    }

    /// <summary>Tek bir portföye tek bir olayı uygular. Hak sahipliği yoksa/uygulanamıyorsa null
    /// döner (skip); başarılıysa insan-okunur bir "etki" metni döner (denetim kaydı için).</summary>
    private async Task<string?> ApplyToPortfolioAsync(
        Guid portfolioId,
        string symbol,
        MarketType market,
        CorporateAction action,
        decimal? cumPrice,
        CancellationToken ct)
    {
        // Hak sahipliği, takvim gününe değil bizim SEANS PENCEREMİZE göre ölçülür — proje sohbeti:
        // BIST sanal seansımız 19:00–09:30 TR açık ve o pencere boyunca hep ÖNCEKİ günün (henüz
        // olayı yansıtmayan) kapanış fiyatıyla işlem görüyor. ActionDate 20'siyse, 20'nin gece
        // yarısından 09:30'a kadar yapılan alımlar da hâlâ eski fiyatladır — hak sahibidirler.
        // 09:30'dan SONRA (bizim seansımız kapandıktan sonra, 20'nin YENİ kapanışı 18:30'da
        // senkronlanana dek) yapılan hiçbir işlem yoktur zaten; dolayısıyla doğru sınır
        // "ActionDate'in kendi günü, saat 09:30 TR" — bir gün öncesi değil.
        var qty = await _uow.Portfolios.GetNetQuantityAsOfAsync(
            portfolioId, symbol, market, EligibilityCutoffUtc(action.ActionDate), ct);
        if (qty <= 0m)
            return null; // ex-date öncesi elinde yoktu, hak sahibi değil

        var portfolio = await _uow.Portfolios.GetByIdWithHoldingsAsync(portfolioId, ct);
        if (portfolio is null)
            return null;

        string effect, notifTitle, notifMessage;

        switch (action.ActionType)
        {
            case CorporateActionType.Dividend:
            {
                var cashAdd = qty * action.Value;
                if (cashAdd <= 0m) return null;
                portfolio.Cash += cashAdd;
                effect = $"Temettü: {qty.ToString("0.####", CultureInfo.InvariantCulture)} adet × " +
                         $"{action.Value.ToString("0.####", CultureInfo.InvariantCulture)} ₺ = " +
                         $"+{cashAdd.ToString("0.##", CultureInfo.InvariantCulture)} ₺ nakit";
                notifTitle = "Temettü hesabınıza yatırıldı";
                notifMessage = $"{symbol}: {cashAdd.ToString("0.##", CultureInfo.InvariantCulture)} ₺ temettü hesabınıza yatırıldı.";
                break;
            }

            case CorporateActionType.BonusIssue:
            {
                var newShares = qty * (action.Value - 1m);
                if (newShares <= 0m) return null;

                var holding = portfolio.Holdings.FirstOrDefault(h => h.Symbol == symbol && h.MarketType == market);
                if (holding is null)
                {
                    portfolio.Holdings.Add(new PortfolioHolding
                    {
                        PortfolioId = portfolio.Id,
                        Symbol = symbol,
                        MarketType = market,
                        Quantity = newShares,
                        AvgCost = 0m,
                    });
                }
                else
                {
                    var newQty = holding.Quantity + newShares;
                    // Toplam maliyet aynı kalır (bedava pay), pay başına maliyet orantılı düşer.
                    holding.AvgCost = newQty > 0m ? holding.AvgCost * holding.Quantity / newQty : holding.AvgCost;
                    holding.Quantity = newQty;
                }

                effect = $"Bedelsiz: {qty.ToString("0.####", CultureInfo.InvariantCulture)} adet × " +
                         $"×{action.Value.ToString("0.####", CultureInfo.InvariantCulture)} = " +
                         $"+{newShares.ToString("0.####", CultureInfo.InvariantCulture)} yeni pay";
                notifTitle = "Bedelsiz pay hesabınıza eklendi";
                notifMessage = $"{symbol}: {newShares.ToString("0.####", CultureInfo.InvariantCulture)} adet bedelsiz pay hesabınıza eklendi.";
                break;
            }

            case CorporateActionType.RightsIssue:
            {
                if (action.SubscriptionPrice is not { } subPrice || subPrice <= 0m || cumPrice is not { } cum || cum <= 0m)
                    return null; // gerekli veri yok, otomatik uygulama — elle incelensin

                var ratio = action.Value;
                var terp = (cum + ratio * subPrice) / (1m + ratio);
                var rightValuePerShare = cum - terp;
                if (rightValuePerShare <= 0m)
                    return null; // teorik olarak negatif/sıfır çıkıyorsa (şüpheli kayıt) uygulama

                var cashAdd = qty * rightValuePerShare;
                portfolio.Cash += cashAdd;
                effect = $"Bedelli (hak satışı gibi): {qty.ToString("0.####", CultureInfo.InvariantCulture)} adet × " +
                         $"{rightValuePerShare.ToString("0.####", CultureInfo.InvariantCulture)} ₺ = " +
                         $"+{cashAdd.ToString("0.##", CultureInfo.InvariantCulture)} ₺ nakit (TERP={terp.ToString("0.####", CultureInfo.InvariantCulture)})";
                notifTitle = "Bedelli hakkınız nakde çevrildi";
                notifMessage = $"{symbol}: bedelli hakkınız karşılığı {cashAdd.ToString("0.##", CultureInfo.InvariantCulture)} ₺ hesabınıza yatırıldı.";
                break;
            }

            default:
                return null;
        }

        portfolio.UpdatedAt = DateTime.UtcNow;
        await _uow.Notifications.AddAsync(new Notification
        {
            Id = Guid.NewGuid(),
            UserId = portfolio.UserId,
            Title = notifTitle,
            Message = notifMessage,
            IsRead = false,
            CreatedAt = DateTime.UtcNow,
        }, ct);
        await _uow.SaveChangesAsync(ct);
        return effect;
    }

    /// <summary>ActionDate'in kendi günü, saat 09:30 TR — bizim seans penceremizin kapanış anı
    /// (bkz. BistTradingHours) — UTC'ye çevrilmiş hali. <see cref="ApplyToPortfolioAsync"/>'daki
    /// açıklamaya bakınız.</summary>
    private static DateTime EligibilityCutoffUtc(DateTime actionDate)
    {
        var turkeyTz = BistTradingHours.ResolveTurkeyTimeZone();
        var cutoffLocal = DateTime.SpecifyKind(actionDate.Date.AddHours(9).AddMinutes(30), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(cutoffLocal, turkeyTz);
    }
}
