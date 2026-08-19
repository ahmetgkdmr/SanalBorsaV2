using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Enums;

namespace SanalBorsa.Application.Common.Services;

/// <summary>
/// Hem ham (Close) hem düzeltilmiş (AdjustedClose) fiyat serisinin doğruluğunu, dış bir kaynağa
/// (Yahoo) hiç ihtiyaç duymadan KENDİ kurumsal olay kayıtlarımıza (KAP/İş Yatırım/Yahoo — bkz.
/// CorporateAction) karşı denetler. Bkz. proje sohbeti: Yahoo'nun "ham" dediği fiyat aslında
/// split-düzeltmeli geliyor VE Yahoo'nun "düzeltilmiş" fiyatı da (kendi bağımsız temettü-bileşikleme
/// hesabı, özellikle BIST'in agresif bedelsiz kültüründe) TradingView'den çok farklı bir tabanda
/// ıraksıyor — ikisi de dış kaynakla karşılaştırmayı güvenilmez kılıyor. Bunun yerine TradingView'e
/// güvenip üç iç tutarlılık kontrolü yapılır:
/// 1) Kayıtlı bir split'in (BonusIssue) çevresinde HAM fiyat gerçekten beklenen oranda hareket
///    etmiş mi? (split raw'da GÖRÜNMELİ — görünmüyorsa TV split'i hiç uygulamamış demektir.)
/// 2) AYNI split'in çevresinde DÜZELTİLMİŞ fiyat pürüzsüz mü kalmış? (split adjusted'da
///    GÖRÜNMEMELİ — görünüyorsa TV'nin kendi düzeltme hesabı split'i ya hiç yutmamış ya da iki kez
///    uygulamış demektir — tam da ROK/HUBB'ın imzası.)
/// 3) Hiçbir kayıtlı olay olmayan bir günde ham fiyat açıklanamaz şekilde sıçramış/düşmüş mü
///    (muhtemelen eksik bir kurumsal olay kaydı ya da TradingView'in tekil hatalı bir barı).
/// </summary>
public sealed class RawPriceActionConsistencyService
{
    /// <summary>Split'in gerçekleşen fiyat oranı, kayıtlı orandan bu kadardan fazla sapıyorsa şüpheli.</summary>
    private const decimal SplitRatioTolerance = 0.15m;

    /// <summary>Düzeltilmiş fiyat, split'in çevresinde bu orandan fazla "sıçrarsa" (pürüzsüz kalması
    /// gerekirken) düzeltmenin split'i doğru yutmadığı anlamına gelir.</summary>
    private const decimal AdjustedContinuityTolerance = 0.15m;

    /// <summary>Kayıtlı hiçbir olayla açıklanamayan %20+ günlük hareket şüpheli sayılır.</summary>
    private const decimal UnexplainedJumpThreshold = 0.20m;

    public RawPriceAuditResult Audit(
        string symbol,
        IReadOnlyList<StockPriceHistory> prices,
        IReadOnlyList<CorporateAction> actions)
    {
        var ordered = prices.OrderBy(p => p.Date).ToList();
        var splitMismatches = new List<SplitMismatch>();
        var adjustedContinuityMismatches = new List<AdjustedContinuityMismatch>();
        var unexplainedJumps = new List<PriceJump>();

        if (ordered.Count < 2)
            return new RawPriceAuditResult(symbol, splitMismatches, adjustedContinuityMismatches, unexplainedJumps);

        // ±1 gün tolerans: ex-date/işlem günü kaymaları, hafta sonu/tatil kaydırmaları için.
        var explainedDates = actions
            .SelectMany(a => new[] { a.ActionDate.Date.AddDays(-1), a.ActionDate.Date, a.ActionDate.Date.AddDays(1) })
            .ToHashSet();

        // 1) ve 2): Kayıtlı split'lerin çevresinde HEM ham fiyat beklenen oranda hareket etmiş mi
        //    HEM DE düzeltilmiş fiyat pürüzsüz kalmış mı? Value = lot çarpanı (2:1 split → Value=2 →
        //    ham fiyat yaklaşık yarıya inmeli, düzeltilmiş fiyat DEĞİŞMEMELİ).
        //    RightsIssue burada KASITLI olarak dahil değil — proje sohbetinde bilinen bir bug
        //    (rüçhan fiyatı 1000 sabit hatası) yüzünden Value alanı bedelli için güvenilmez
        //    (bkz. TimeMachineCalculator.EmpiricalLotMultiplier, aynı nedenle ampirik türetiyor).
        foreach (var action in actions.Where(a => a.ActionType == CorporateActionType.BonusIssue))
        {
            var before = ordered.LastOrDefault(p => p.Date.Date < action.ActionDate.Date);
            var after = ordered.FirstOrDefault(p => p.Date.Date >= action.ActionDate.Date);
            if (before is null || after is null)
                continue;

            if (before.Close > 0m && after.Close > 0m && action.Value > 0m)
            {
                var actualRatio = before.Close / after.Close;
                var expectedRatio = action.Value;
                var diff = Math.Abs(actualRatio - expectedRatio) / expectedRatio;

                if (diff > SplitRatioTolerance)
                {
                    splitMismatches.Add(new SplitMismatch(
                        action.ActionDate.Date, expectedRatio, actualRatio, before.Close, after.Close));
                }
            }

            if (before.AdjustedClose > 0m && after.AdjustedClose > 0m)
            {
                var adjRatio = before.AdjustedClose / after.AdjustedClose;
                if (Math.Abs(adjRatio - 1m) > AdjustedContinuityTolerance)
                {
                    adjustedContinuityMismatches.Add(new AdjustedContinuityMismatch(
                        action.ActionDate.Date, adjRatio, before.AdjustedClose, after.AdjustedClose));
                }
            }
        }

        // 3) Kayıtlı hiçbir olay olmayan günde ham fiyat açıklanamaz şekilde sıçrama var mı?
        for (var i = 1; i < ordered.Count; i++)
        {
            var prev = ordered[i - 1];
            var cur = ordered[i];
            if (prev.Close <= 0m || cur.Close <= 0m) continue;
            if (explainedDates.Contains(cur.Date.Date)) continue;

            var ratio = cur.Close / prev.Close;
            if (ratio < 1m - UnexplainedJumpThreshold || ratio > 1m + UnexplainedJumpThreshold)
            {
                unexplainedJumps.Add(new PriceJump(cur.Date.Date, prev.Close, cur.Close, (ratio - 1m) * 100m));
            }
        }

        return new RawPriceAuditResult(symbol, splitMismatches, adjustedContinuityMismatches, unexplainedJumps);
    }
}

public record SplitMismatch(
    DateTime ActionDate, decimal ExpectedRatio, decimal ActualRatio, decimal PriceBefore, decimal PriceAfter);

public record AdjustedContinuityMismatch(
    DateTime ActionDate, decimal ActualRatio, decimal AdjustedBefore, decimal AdjustedAfter);

public record PriceJump(DateTime Date, decimal PrevClose, decimal Close, decimal PctChange);

public record RawPriceAuditResult(
    string Symbol,
    IReadOnlyList<SplitMismatch> SplitMismatches,
    IReadOnlyList<AdjustedContinuityMismatch> AdjustedContinuityMismatches,
    IReadOnlyList<PriceJump> UnexplainedJumps)
{
    public bool HasIssues =>
        SplitMismatches.Count > 0 || AdjustedContinuityMismatches.Count > 0 || UnexplainedJumps.Count > 0;
}
