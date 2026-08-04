using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Enums;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Stocks.Commands.SyncUsCorporateActions;

public class SyncUsCorporateActionsCommandHandler
    : IRequestHandler<SyncUsCorporateActionsCommand, SyncUsCorporateActionsResult>
{
    private const int DelayMs = 250;

    /// <summary>Split doğrulaması: beklenen fiyat oranından bu kadar sapma toleranslı sayılır.</summary>
    private const decimal SplitRatioTolerance = 0.25m;

    private readonly IUnitOfWork _uow;
    private readonly IYahooFinanceService _yahoo;
    private readonly ILogger<SyncUsCorporateActionsCommandHandler> _logger;

    public SyncUsCorporateActionsCommandHandler(
        IUnitOfWork uow,
        IYahooFinanceService yahoo,
        ILogger<SyncUsCorporateActionsCommandHandler> logger)
    {
        _uow = uow;
        _yahoo = yahoo;
        _logger = logger;
    }

    public async Task<SyncUsCorporateActionsResult> Handle(
        SyncUsCorporateActionsCommand request,
        CancellationToken cancellationToken)
    {
        int processed = 0, skipped = 0, added = 0, failed = 0;

        List<Stock> stocks;
        if (!string.IsNullOrWhiteSpace(request.Symbol))
        {
            var one = await _uow.Stocks.GetBySymbolAsync(
                request.Symbol.Trim().ToUpperInvariant(), cancellationToken, MarketType.UsStocks);
            stocks = one is null ? [] : [one];
        }
        else
        {
            stocks = (await _uow.Stocks.GetAllActiveAsync(cancellationToken, MarketType.UsStocks))
                .Where(s => s.MarketType == MarketType.UsStocks)
                .OrderBy(s => s.Symbol)
                .ToList();
        }

        _logger.LogInformation("ABD kurumsal işlem sync başladı — {Count} hisse (Yahoo Finance)", stocks.Count);

        foreach (var stock in stocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            processed++;

            try
            {
                var raw = await _yahoo.GetCorporateActionsAsync(stock.YahooSymbol, cancellationToken);
                if (raw.Count == 0)
                {
                    skipped++;
                    await Task.Delay(DelayMs, cancellationToken);
                    continue;
                }

                // Fiyat artık TradingView'dan HAM (split'e göre ayarlanmamış) geliyor — bu yüzden
                // split olaylarını (BonusIssue) tekrar CorporateAction olarak saklıyoruz. Yahoo tek
                // kaynak olduğu için (BIST'teki KAP+İş Yatırım çapraz doğrulaması burada yok), her
                // split'i kendi ham fiyat serimizdeki gerçek sıçramayla doğruluyoruz. Ama bu komut
                // fiyat senkronundan ÖNCE de çalışabilir (split tarihini PriceAnomalyGuard'ın anomali
                // muafiyeti için önceden bilmesi gerekir) — o an henüz fiyat verisi olmayabilir.
                // Üç durum: fiyatla doğrulandı (ekle), fiyatla çelişiyor (reddet/sil), henüz fiyat
                // yok (bilinmiyor — şimdilik güvenerek ekle, fiyat gelince bir sonraki çalıştırmada
                // otomatik doğrulanır/silinir).
                var incoming = new List<CorporateAction>();
                var rejectedSplitDates = new HashSet<DateTime>();
                foreach (var action in raw)
                {
                    if (action.ActionType != CorporateActionType.BonusIssue)
                    {
                        incoming.Add(action);
                        continue;
                    }

                    var confirmed = await IsSplitConfirmedByPriceAsync(stock.Id, action, cancellationToken);
                    if (confirmed == false)
                    {
                        rejectedSplitDates.Add(action.ActionDate.Date);
                        _logger.LogWarning(
                            "ABD split doğrulanamadı, reddedildi: {Symbol} {Date:yyyy-MM-dd} oran={Value} — fiyat serisinde karşılık gelen sıçrama yok",
                            stock.Symbol, action.ActionDate, action.Value);
                        continue;
                    }

                    incoming.Add(action);
                }

                if (rejectedSplitDates.Count > 0)
                {
                    var toRemove = (await _uow.CorporateActions.GetByStockIdAndTypeAsync(
                            stock.Id, CorporateActionType.BonusIssue, cancellationToken))
                        .Where(a => rejectedSplitDates.Contains(a.ActionDate.Date))
                        .ToList();
                    if (toRemove.Count > 0)
                        _uow.CorporateActions.RemoveRange(toRemove);
                }

                if (incoming.Count == 0)
                {
                    skipped++;
                    await _uow.SaveChangesAsync(cancellationToken);
                    _uow.ClearChanges();
                    await Task.Delay(DelayMs, cancellationToken);
                    continue;
                }

                // Yahoo'nun temettü tutarı, o tarihten SONRA gerçekleşen split'lere göre (ileri VE
                // ters split) önceden ayarlanmış geliyor — tıpkı AdjustedClose gibi "bugünün hisse
                // sayısı" cinsinden gösteriyor. Biz hâlâ o tarihteki HAM (split-öncesi) lot sayısını
                // kullandığımız için bu ayarlamayı geri alıyoruz: gelecekteki split oranlarının
                // çarpımıyla çarpıyoruz. Doğrulanmış örnek: Citigroup'un 2007 temettüsü Yahoo'dan
                // $5.40 geliyordu — 2011'deki 1:10 ters split'e göre ayarlanmıştı; ×0.1 ile gerçek
                // ham değeri olan $0.54'e dönüyor. Bu düzeltme yapılmadan reinvest simülasyonu
                // ters split'i olan hisselerde (GE, C gibi) onlarca kat şişiyordu.
                var confirmedSplits = incoming
                    .Where(a => a.ActionType == CorporateActionType.BonusIssue)
                    .OrderBy(a => a.ActionDate)
                    .ToList();
                foreach (var div in incoming.Where(a => a.ActionType == CorporateActionType.Dividend))
                {
                    var futureMultiplier = confirmedSplits
                        .Where(s => s.ActionDate.Date > div.ActionDate.Date)
                        .Aggregate(1m, (acc, s) => acc * s.Value);
                    if (futureMultiplier > 0m && futureMultiplier != 1m)
                        div.Value = Math.Round(div.Value * futureMultiplier, 6);
                }

                // Mevcut temettü satırları önceki (düzeltme öncesi) yanlış tutarlarla kaydedilmiş
                // olabilir — ExistsAsync ile "zaten var" deyip atlarsak asla düzelmezler. Bu yüzden
                // temettüler için dedupe yerine sil + yeniden yaz yapıyoruz (split'ler için mevcut
                // ExistsAsync dedup deseni aynen korunuyor — onlar zaten doğru/değişmiyor).
                if (incoming.Any(a => a.ActionType == CorporateActionType.Dividend))
                {
                    var staleDividends = await _uow.CorporateActions.GetByStockIdAndTypeAsync(
                        stock.Id, CorporateActionType.Dividend, cancellationToken);
                    if (staleDividends.Count > 0)
                        _uow.CorporateActions.RemoveRange(staleDividends);
                }

                foreach (var action in incoming)
                {
                    action.StockId = stock.Id;

                    if (action.ActionType != CorporateActionType.Dividend)
                    {
                        var exists = await _uow.CorporateActions.ExistsAsync(
                            stock.Id, action.ActionDate, action.ActionType, cancellationToken);
                        if (exists)
                            continue;
                    }

                    await _uow.CorporateActions.AddAsync(action, cancellationToken);
                    added++;
                }

                await _uow.SaveChangesAsync(cancellationToken);
                _uow.ClearChanges();

                _logger.LogInformation(
                    "ABD kurumsal işlem sync [{Current}/{Total}] — {Symbol}: {Count} olay çekildi",
                    processed, stocks.Count, stock.Symbol, incoming.Count);

                await Task.Delay(DelayMs, cancellationToken);
            }
            catch (Exception ex)
            {
                failed++;
                _uow.ClearChanges();
                _logger.LogError(ex, "ABD kurumsal işlem sync başarısız: {Symbol}", stock.Symbol);
            }
        }

        _logger.LogInformation(
            "ABD kurumsal işlem sync bitti — processed={Processed}, skipped={Skipped}, added={Added}, failed={Failed}",
            processed, skipped, added, failed);

        return new SyncUsCorporateActionsResult(processed, skipped, added, failed);
    }

    /// <summary>
    /// Yahoo'nun bildirdiği split oranını (Value = numerator/denominator) hissenin kendi ham fiyat
    /// serisindeki gerçek sıçramayla karşılaştırır. <c>true</c>: fiyatla doğrulandı. <c>false</c>:
    /// fiyat verisi var ama oran uyuşmuyor — reddedilmeli. <c>null</c>: bu tarih aralığında henüz
    /// fiyat verisi yok — doğrulama şimdilik yapılamıyor (fiyat sync'inden önce çalışan ilk geçiş).
    /// </summary>
    private async Task<bool?> IsSplitConfirmedByPriceAsync(
        int stockId, CorporateAction action, CancellationToken ct)
    {
        if (action.Value <= 0)
            return false;

        var bars = await _uow.PriceHistories.GetByStockIdAsync(
            stockId, action.ActionDate.AddDays(-7), action.ActionDate.AddDays(7), ct);
        if (bars.Count < 2)
            return null;

        // "after" için tam split gününü değil birkaç gün ilerisini kullanıyoruz: split günü,
        // PriceAnomalyGuard bu split'i henüz bilmediği bir önceki koşuda placeholder (önceki günün
        // düz kapanışı) yazmış olabilir — o durumda tam o günü karşılaştırmak yanlış negatif üretir.
        var before = bars.Where(b => b.Date.Date < action.ActionDate.Date)
            .OrderByDescending(b => b.Date).FirstOrDefault();
        var after = bars.Where(b => b.Date.Date >= action.ActionDate.Date.AddDays(1))
            .OrderBy(b => b.Date).FirstOrDefault();
        if (before is null || after is null || before.Close <= 0)
            return null;

        var actualRatio = after.Close / before.Close;
        var expectedRatio = 1m / action.Value;
        var deviation = Math.Abs(actualRatio - expectedRatio) / expectedRatio;
        return deviation <= SplitRatioTolerance;
    }
}
