using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Enums;

namespace SanalBorsa.Application.Common;

/// <summary>
/// AdjustedClose senkron job'ları (BIST/ABD) TradingView'den taze bir seri çektiğinde, o serinin
/// baştan sona ima ettiği getiri ham (Close) fiyatın aynı aralıktaki getirisiyle taban tabana zıt
/// işaretteyse (biri büyük kazanç biri büyük kayıp diyorsa) — kaynak o an bozuk bir yanıt vermiş
/// demektir (bkz. proje sohbeti: ROK/HUBB'ta böyle bir bozuk yanıt vardı, birkaç dakika sonra
/// TradingView'e tekrar sorulunca kendiliğinden düzeldi — geçici bir "kötü anlık görüntü").
/// Böyle bir seri hiç yazılmamalı; çağıran ~1 saat sonra tek seferlik tekrar dener.
///
/// AMA: gerçek büyük bir bedelsiz/bedelli varsa bu "zıt yönlü getiri" imzası aslında BEKLENEN VE
/// DOĞRU davranış — ham fiyat mekanik çöker, düzeltilmiş fiyat gerçek getiriyi gösterir (bkz. proje
/// sohbeti: A1CAP ×5 bedelsiz — TV'nin verisi Yahoo'nun split kaydıyla da doğrulandı, ama bu kontrol
/// onu "şüpheli" diye reddedip A1CAP'i alım-satıma kapattı). O yüzden aralıkta kayıtlı büyük bir
/// olay (BonusIssue/RightsIssue) varsa zıtlığı hiç şüpheli saymıyoruz — sadece kaydımızda HİÇBİR
/// olay yokken böyle bir zıtlık çıkarsa (ROK/HUBB'daki gibi gerçekten açıklanamaz) reddediyoruz.
/// </summary>
public static class AdjustedCloseSanityCheck
{
    private const decimal SuspectThreshold = 20m; // %20 — her iki yönde de "büyük" hareket say

    public static bool IsPlausible(
        IReadOnlyList<StockPriceHistory> existingRawPrices,
        IReadOnlyDictionary<DateTime, decimal> freshAdjustedCloses,
        IReadOnlyList<CorporateAction>? corporateActions = null)
    {
        if (freshAdjustedCloses.Count < 2 || existingRawPrices.Count < 2)
            return true; // karşılaştıracak yeterli veri yok — reddetme, olduğu gibi kabul et

        var oldestDate = freshAdjustedCloses.Keys.Min();
        var newestDate = freshAdjustedCloses.Keys.Max();
        if (oldestDate >= newestDate)
            return true;

        var rawOldest = existingRawPrices
            .Where(p => p.Date.Date >= oldestDate)
            .OrderBy(p => p.Date)
            .FirstOrDefault();
        var rawNewest = existingRawPrices
            .Where(p => p.Date.Date <= newestDate)
            .OrderByDescending(p => p.Date)
            .FirstOrDefault();

        if (rawOldest is null || rawNewest is null || rawOldest.Close <= 0m || rawNewest.Close <= 0m)
            return true;

        var adjOldest = freshAdjustedCloses[oldestDate];
        var adjNewest = freshAdjustedCloses[newestDate];
        if (adjOldest <= 0m || adjNewest <= 0m)
            return true;

        var rawReturnPct = (rawNewest.Close - rawOldest.Close) / rawOldest.Close * 100m;
        var adjReturnPct = (adjNewest - adjOldest) / adjOldest * 100m;

        var suspicious = Math.Sign(rawReturnPct) != Math.Sign(adjReturnPct) &&
            Math.Abs(rawReturnPct) > SuspectThreshold && Math.Abs(adjReturnPct) > SuspectThreshold;

        if (suspicious && corporateActions is not null)
        {
            var hasExplainingAction = corporateActions.Any(a =>
                a.ActionType is CorporateActionType.BonusIssue or CorporateActionType.RightsIssue &&
                a.ActionDate.Date >= oldestDate && a.ActionDate.Date <= newestDate);
            if (hasExplainingAction)
                suspicious = false;
        }

        return !suspicious;
    }
}
