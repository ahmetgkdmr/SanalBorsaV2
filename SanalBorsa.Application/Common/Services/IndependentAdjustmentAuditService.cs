using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Enums;

namespace SanalBorsa.Application.Common.Services;

/// <summary>
/// TradingView'e hiç güvenmeden, SADECE bizim veritabanımızdaki ham fiyat (Close) + kurumsal olay
/// kayıtlarını (Dividend/BonusIssue/RightsIssue) kullanarak, en eski günden bugüne doğru KENDİ
/// düzeltilmiş fiyat serimizi baştan hesaplar ve TradingView'in bize verdiği AdjustedClose ile
/// gün gün karşılaştırır. Bkz. proje sohbeti: bu, dış bir kaynakla (Yahoo) karşılaştırmaktan daha
/// isabetli — çünkü iki tarafın da AYNI kayıtlı olayları kullanıp kullanmadığını doğrudan test eder,
/// vendor-farkı gürültüsü (BIST'in agresif bedelsiz kültüründen kaynaklanan) karışmaz.
///
/// Hesaplama, en güncel günden geriye doğru yürür (adjusted(bugün) = raw(bugün) sabit nokta):
/// - BonusIssue (bedelsiz/split): kayıtlı Value (lot çarpanı) kullanılır — bu alan güvenilir
///   (bkz. RawPriceActionConsistencyService.SplitMismatch, ayrıca doğrulanıyor).
/// - RightsIssue (bedelli): kayıtlı Value KASITLI OLARAK kullanılmaz — bilinen bir veri kalitesi
///   sorunu var (rüçhan fiyatı sabit 1000 hatası, bkz. TimeMachineCalculator.EmpiricalLotMultiplier
///   — aynı sebeple orada da kayıtlı değer yerine fiyat hareketinden ampirik türetiliyor). Burada da
///   aynı ampirik yöntem kullanılır: gerçek çarpan, o günün ham fiyat önce/sonra oranından çıkarılır.
/// - Dividend (temettü): standart toplam-getiri düzeltme formülü — factor = 1 - temettü/ex-date
///   öncesi kapanış.
/// </summary>
public sealed class IndependentAdjustmentAuditService
{
    /// <summary>Bizim hesapladığımız ile TradingView'in verdiği bu kadardan fazla ayrışırsa şüpheli.</summary>
    private const decimal DiffThreshold = 0.05m; // %5

    public IndependentAdjustmentResult Audit(
        string symbol,
        IReadOnlyList<StockPriceHistory> prices,
        IReadOnlyList<CorporateAction> actions)
    {
        var ordered = prices.OrderBy(p => p.Date).ToList();
        var mismatches = new List<AdjustmentMismatch>();

        if (ordered.Count == 0)
            return new IndependentAdjustmentResult(symbol, mismatches, DaysCompared: 0);

        var actionsDesc = actions
            .Where(a => a.ActionType is CorporateActionType.BonusIssue or CorporateActionType.RightsIssue
                or CorporateActionType.Dividend)
            .OrderByDescending(a => a.ActionDate)
            .ToList();

        var cumulativeFactor = 1m;
        var actionIdx = 0;
        var compared = 0;

        // En güncel günden en eskiye doğru yürü.
        for (var i = ordered.Count - 1; i >= 0; i--)
        {
            var bar = ordered[i];

            // Bu barın tarihinden SONRAKİ (henüz uygulanmamış) tüm olayları, tarih sırasına göre
            // en yeniden en eskiye doğru uygula — geriye doğru yürüdüğümüz için bu barın "öncesinde"
            // kalan olaylar bundan sonraki iterasyonlarda işlenecek.
            while (actionIdx < actionsDesc.Count && actionsDesc[actionIdx].ActionDate.Date > bar.Date.Date)
            {
                var action = actionsDesc[actionIdx];
                var factor = ComputeFactor(action, ordered);
                if (factor is > 0m)
                    cumulativeFactor *= factor.Value;
                actionIdx++;
            }

            if (bar.Close <= 0m || bar.AdjustedClose <= 0m)
                continue;

            var computed = bar.Close * cumulativeFactor;
            compared++;

            var ratioDiff = Math.Abs(computed / bar.AdjustedClose - 1m);
            if (ratioDiff > DiffThreshold)
            {
                mismatches.Add(new AdjustmentMismatch(bar.Date.Date, computed, bar.AdjustedClose, ratioDiff));
            }
        }

        return new IndependentAdjustmentResult(symbol, mismatches, compared);
    }

    /// <summary>Bu olayın ÖNCESİNDEKİ tüm günlere uygulanacak kümülatif çarpan.</summary>
    private static decimal? ComputeFactor(CorporateAction action, IReadOnlyList<StockPriceHistory> ordered)
    {
        switch (action.ActionType)
        {
            case CorporateActionType.BonusIssue when action.Value > 0m:
                // Value = ham fiyat önce/sonra oranı (2:1 split → Value=2) — kayıtlı değer güvenilir.
                return 1m / action.Value;

            case CorporateActionType.RightsIssue:
            {
                // Kayıtlı Value KASITLI OLARAK kullanılmıyor (bkz. sınıf üstü not) — ham fiyatın
                // gerçek önce/sonra oranından ampirik olarak türetiliyor. Kayıtlı ActionDate'e TAM
                // kilitlenmek yerine ±3 günlük bir pencerede ARANIYOR — proje sohbeti: RALYH'te
                // kayıtlı tarih (26 Ağustos) ile gerçek fiyat sıçramasının olduğu gün (27 Ağustos)
                // arasında 1 günlük kayma bulundu; tam tarihe kilitlenmek bu kaymayı kaçırıp
                // çarpanı hiç uygulamıyordu (bkz. FindLargestJumpNear).
                var jump = FindLargestJumpNear(ordered, action.ActionDate.Date, windowDays: 3);
                return jump is { } j && j.Before.Close > 0m && j.After.Close > 0m
                    ? j.After.Close / j.Before.Close
                    : null;
            }

            case CorporateActionType.Dividend when action.Value > 0m:
            {
                var before = ordered.LastOrDefault(p => p.Date.Date < action.ActionDate.Date);
                if (before is null || before.Close <= 0m || before.Close <= action.Value)
                    return null;
                return 1m - action.Value / before.Close;
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// Kayıtlı tarihin ±windowDays çevresindeki tüm ardışık gün çiftlerinden, en büyük tek-günlük
    /// fiyat sıçramasını (muhtemelen gerçek bedelli/split hareketi) bulur. %10'un altındaki en
    /// büyük fark muhtemelen sıradan piyasa oynaklığı sayılır, olay olarak kabul edilmez.
    /// </summary>
    private static (StockPriceHistory Before, StockPriceHistory After)? FindLargestJumpNear(
        IReadOnlyList<StockPriceHistory> ordered, DateTime actionDate, int windowDays)
    {
        var from = actionDate.AddDays(-windowDays);
        var to = actionDate.AddDays(windowDays);
        var windowBars = ordered.Where(p => p.Date.Date >= from && p.Date.Date <= to).ToList();
        if (windowBars.Count < 2) return null;

        (StockPriceHistory Before, StockPriceHistory After)? best = null;
        var bestDeviation = 0m;

        for (var i = 1; i < windowBars.Count; i++)
        {
            var before = windowBars[i - 1];
            var after = windowBars[i];
            if (before.Close <= 0m || after.Close <= 0m) continue;

            var deviation = Math.Abs(after.Close / before.Close - 1m);
            if (deviation > bestDeviation)
            {
                bestDeviation = deviation;
                best = (before, after);
            }
        }

        return bestDeviation > 0.10m ? best : null;
    }
}

public record AdjustmentMismatch(DateTime Date, decimal Computed, decimal Actual, decimal RatioDiff);

public record IndependentAdjustmentResult(
    string Symbol,
    IReadOnlyList<AdjustmentMismatch> Mismatches,
    int DaysCompared)
{
    public bool HasIssues => Mismatches.Count > 0;
}
