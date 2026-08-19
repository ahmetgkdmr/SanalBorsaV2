using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Enums;

namespace SanalBorsa.Application.Common.Services;

/// <summary>
/// TradingView'in kendi düzeltme sınırını (hangi gün "öncesi/sonrası" saydığını) TAHMİN ETMEK
/// yerine, TV'nin ZATEN bize verdiği iki sayıdan (Close, AdjustedClose) doğrudan okur. Bkz. proje
/// sohbeti: IndependentAdjustmentAuditService, sınır gününü kayıtlı ActionDate'e göre tahmin etmeye
/// çalışıyordu (RALYH'te 1 gün kayma bulundu) — bu servis o tahmini tamamen ortadan kaldırıyor.
///
/// Mantık: her gün için impliedFactor(t) = AdjustedClose(t) / Close(t) hesaplanır — bu, TV'nin o
/// gün için uyguladığı kümülatif indirim oranıdır. Bu oran SADECE TV'nin kendi kararıyla bir olayı
/// "yuttuğu" günlerde basamak atlar (sıçrar), aradaki günlerde sabit kalır. Bu sıçrama noktalarını
/// bulup KENDİ kurumsal olay kayıtlarımızla (sadece büyük etkili BonusIssue/RightsIssue — temettü
/// etkisi genelde eşiğin altında kalıp gürültüyle karışır) eşleştiriyoruz:
/// 1) Kayıtlı bir olayın ±3 gün çevresinde TV'de GERÇEKTEN bir sıçrama var mı? (yoksa: TV o olayı
///    hiç uygulamamış olabilir.)
/// 2) TV'de sıçrama var ama büyüklüğü kayıtlı olayın beklediğinden çok farklıysa: uyuşmazlık.
/// 3) TV'de sıçrama var ama hiçbir kayıtlı olayla eşleşmiyorsa: bizim kaydımızda eksik bir olay
///    olabilir YA DA TV'nin tekil bir düzeltme hatası.
/// </summary>
public sealed class TvImpliedFactorAuditService
{
    /// <summary>İki gün arası implied factor bu kadardan fazla değişirse "basamak" (step) sayılır.</summary>
    private const decimal StepDetectionThreshold = 0.02m; // %2

    /// <summary>TV'nin uyguladığı basamak büyüklüğü, kayıtlı olayın beklediğinden bu kadardan
    /// fazla saparsa uyuşmazlık sayılır.</summary>
    private const decimal MagnitudeTolerance = 0.15m; // %15

    /// <summary>Kayıtlı olay ile TV basamağını eşleştirmek için izin verilen gün farkı.</summary>
    private const int MatchWindowDays = 3;

    /// <summary>KAP elektronik kamu aydınlatma sistemi Eylül 2009'da başladı; doğrudan test edildi
    /// (2003, 2006 sorguları tamamen boş döndü) — bu tarihten önce hiçbir kaynakta (KAP, İş Yatırım)
    /// yapılandırılmış kurumsal olay kaydı yok. Bu tarihten önceki basamaklar/olaylar ne doğrulanabilir
    /// ne çürütülebilir, o yüzden denetime hiç dahil edilmiyor (gürültü — bkz. proje sohbeti).</summary>
    private static readonly DateTime DataCoverageStart = new(2010, 1, 1);

    public ImpliedFactorAuditResult Audit(
        string symbol,
        IReadOnlyList<StockPriceHistory> prices,
        IReadOnlyList<CorporateAction> actions)
    {
        // Hacim=0 (işlem durdurulmuş/donmuş) barlar dışarıda bırakılıyor — proje sohbeti: ADEL
        // 2012-10-16'da BİST işlemi durdurdu (gayrimenkul satışı haberi sonrası aşırı fiyat hareketi
        // incelemesi), o tek donmuş günde TV'nin implied factor'ü bir anlığına anormal bir değere
        // sıçrayıp bir sonraki gerçek işlem gününde kendiliğinden eski haline dönüyordu — kurumsal
        // olayla hiç ilgisi yoktu, TV'nin donmuş barlarda ürettiği tek-günlük bir hesaplama tuhaflığı.
        var ordered = prices
            .Where(p => p.Close > 0m && p.AdjustedClose > 0m && p.Volume > 0)
            .OrderBy(p => p.Date)
            .ToList();

        var unexplainedSteps = new List<ImpliedStep>();
        var missingSteps = new List<MissingStep>();
        var magnitudeMismatches = new List<MagnitudeMismatch>();

        if (ordered.Count < 2)
            return new ImpliedFactorAuditResult(symbol, unexplainedSteps, missingSteps, magnitudeMismatches);

        // 1) TV'nin kendi implied factor serisindeki basamakları bul.
        var steps = new List<(DateTime Date, decimal Before, decimal After)>();
        var prevFactor = ordered[0].AdjustedClose / ordered[0].Close;
        for (var i = 1; i < ordered.Count; i++)
        {
            var factor = ordered[i].AdjustedClose / ordered[i].Close;
            if (prevFactor > 0m && Math.Abs(factor / prevFactor - 1m) > StepDetectionThreshold)
            {
                steps.Add((ordered[i].Date.Date, prevFactor, factor));
            }
            prevFactor = factor;
        }

        // 2010 öncesi hiçbir kaynakta doğrulanabilir kurumsal olay kaydı yok (bkz. DataCoverageStart) —
        // o dönemin basamaklarını denetimden tamamen çıkar, ne "eksik" ne "eşleşmeyen" olarak raporlanmasın.
        steps = steps.Where(s => s.Date >= DataCoverageStart).ToList();

        // Sadece büyük etkili olayları (BonusIssue/RightsIssue) eşleştir — temettü etkisi genelde
        // %2'lik basamak eşiğinin altında kalıp normal fiyat oynaklığıyla karışır, ayrı ele alınmalı.
        var bigActions = actions
            .Where(a => a.ActionType is CorporateActionType.BonusIssue or CorporateActionType.RightsIssue)
            .Where(a => a.ActionDate.Date >= DataCoverageStart)
            .OrderBy(a => a.ActionDate)
            .ToList();

        var matchedStepIdx = new HashSet<int>();

        foreach (var action in bigActions)
        {
            var candidates = steps
                .Select((s, idx) => (Step: s, Idx: idx))
                .Where(x => Math.Abs((x.Step.Date - action.ActionDate.Date).TotalDays) <= MatchWindowDays)
                .OrderBy(x => Math.Abs((x.Step.Date - action.ActionDate.Date).TotalDays))
                .ToList();

            if (candidates.Count == 0)
            {
                // Kayıtlı olayın TV'nin düzeltmesinde karşılığı yok — ama HAM fiyat o tarihte
                // gerçekten düşmüş/artmış mı diye ayrıca bakıyoruz (proje sohbeti: AFYON'da manuel
                // yapılan bu kontrol, TV'nin ham verisinin de o olayı hiç yansıtmadığını ortaya
                // çıkardı — yani sorun sadece AdjustedClose'ta değil, ham fiyatın kendisindeydi).
                var rawJump = FindLargestRawJumpNear(ordered, action.ActionDate.Date, MatchWindowDays);
                decimal? rawRatio = rawJump is { } rj && rj.Before.Close > 0m
                    ? rj.After.Close / rj.Before.Close
                    : null;

                bool? rawMatchesAction = null;
                if (rawRatio is not null && action.ActionType == CorporateActionType.BonusIssue && action.Value > 0m)
                {
                    var expectedRawRatio = 1m / action.Value;
                    rawMatchesAction = Math.Abs(rawRatio.Value / expectedRawRatio - 1m) <= MagnitudeTolerance;
                }

                missingSteps.Add(new MissingStep(
                    action.ActionDate.Date, action.ActionType.ToString(), action.Value, rawRatio, rawMatchesAction));
                continue;
            }

            var (step, idx) = candidates[0];
            matchedStepIdx.Add(idx);

            if (action.ActionType == CorporateActionType.BonusIssue && action.Value > 0m && step.Before > 0m)
            {
                // factor_önce/factor_sonra = 1/Value (düzeltilmiş fiyat pürüzsüz kalsın diye, ham
                // fiyat Value kadar düştüğünde) → factor_sonra/factor_önce = Value. Önceki sürümde
                // bu ters yazılmıştı (1/Value) — proje sohbeti: THYAO/AEFES/ADEL'de "beklenen×0,5
                // TV×2,94" gibi tam tersi çıkan sonuçlar bu ters formülü ele verdi.
                var actualRatio = step.After / step.Before;
                var expectedRatio = action.Value;
                var diff = Math.Abs(actualRatio / expectedRatio - 1m);
                if (diff > MagnitudeTolerance)
                {
                    magnitudeMismatches.Add(new MagnitudeMismatch(
                        action.ActionDate.Date, step.Date, expectedRatio, actualRatio));
                }
            }
        }

        // 2) Henüz eşleşmemiş basamakları temettü kayıtlarına karşı da dene — SADECE varlık
        //    kontrolü (büyüklük değil): temettü düzeltme formülünün TV'nin tam kendi konvansiyonuyla
        //    (ex-date referans fiyatı vb.) örtüşüp örtüşmediğini bilmiyoruz (bkz. proje sohbeti —
        //    ROK'ta 179 temettü olayında ufak yakınsama farklarının bile birikip büyük sapmaya
        //    dönüştüğü görüldü), o yüzden burada büyüklüğü değil sadece "yakında kayıtlı bir temettü
        //    var mı" doğrulanıyor — eşleşirse "temettüden kaynaklı, muhtemelen zararsız" sayılır.
        var dividendDates = actions
            .Where(a => a.ActionType == CorporateActionType.Dividend)
            .Where(a => a.ActionDate.Date >= DataCoverageStart)
            .Select(a => a.ActionDate.Date)
            .ToList();

        for (var idx = 0; idx < steps.Count; idx++)
        {
            if (matchedStepIdx.Contains(idx)) continue;
            if (dividendDates.Any(d => Math.Abs((d - steps[idx].Date).TotalDays) <= MatchWindowDays))
                continue;

            // Ham fiyat da o tarihte gerçekten sıçramış mı? Sıçramışsa muhtemelen kaydımızda
            // eksik bir olay var (gerçek bir şey oldu, sadece bizde kaydı yok); sıçramamışsa
            // (ham düz) sorun muhtemelen TV'nin AdjustedClose hesabına özgü, ham fiyatla ilgisi yok
            // (bkz. ADEL 2012-10-16 — ama o zaten hacim=0 filtresiyle artık burada elenmiş oluyor).
            var rawJump = FindLargestRawJumpNear(ordered, steps[idx].Date, MatchWindowDays);
            decimal? rawRatio = rawJump is { } rj && rj.Before.Close > 0m
                ? rj.After.Close / rj.Before.Close
                : null;

            unexplainedSteps.Add(new ImpliedStep(steps[idx].Date, steps[idx].Before, steps[idx].After, rawRatio));
        }

        return new ImpliedFactorAuditResult(symbol, unexplainedSteps, missingSteps, magnitudeMismatches);
    }

    /// <summary>
    /// Kayıtlı tarihin ±windowDays çevresindeki tüm ardışık gün çiftlerinden, en büyük tek-günlük
    /// HAM fiyat sıçramasını bulur. %10'un altındaki en büyük fark muhtemelen sıradan piyasa
    /// oynaklığı sayılır, olay olarak kabul edilmez.
    /// </summary>
    private static (StockPriceHistory Before, StockPriceHistory After)? FindLargestRawJumpNear(
        IReadOnlyList<StockPriceHistory> ordered, DateTime date, int windowDays)
    {
        var from = date.AddDays(-windowDays);
        var to = date.AddDays(windowDays);
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

public record ImpliedStep(DateTime Date, decimal FactorBefore, decimal FactorAfter, decimal? RawRatio = null)
{
    public decimal Ratio => FactorBefore > 0m ? FactorAfter / FactorBefore : 0m;
}

/// <param name="RawRatio">O tarih civarında HAM fiyatın gerçek önce/sonra oranı (varsa) — TV'nin
/// düzeltmesinde karşılığı yoksa bile ham fiyat gerçekten sıçramış mı diye ayrıca bakılır.</param>
/// <param name="RawMatchesAction">Sadece BonusIssue için: ham fiyattaki gerçek oran, kayıtlı olayın
/// beklediği oranla (±%15) örtüşüyor mu? true → ham fiyat kayıtla tutarlı, sorun sadece TV'nin
/// AdjustedClose'unda (bizim tarafta düzeltilebilir, A1CAP tipi). false → ham fiyat da kayıtla
/// tutarsız, ham verinin kendisi şüpheli (AFYON tipi, düzeltilemez — TV'nin kaynak verisi eksik).
/// null → RightsIssue (kayıtlı Value zaten güvenilmiyor) ya da civarda ham fiyat verisi yok.</param>
public record MissingStep(
    DateTime ActionDate, string ActionType, decimal Value,
    decimal? RawRatio = null, bool? RawMatchesAction = null);

public record MagnitudeMismatch(DateTime ActionDate, DateTime StepDate, decimal ExpectedRatio, decimal ActualRatio);

public record ImpliedFactorAuditResult(
    string Symbol,
    IReadOnlyList<ImpliedStep> UnexplainedSteps,
    IReadOnlyList<MissingStep> MissingSteps,
    IReadOnlyList<MagnitudeMismatch> MagnitudeMismatches)
{
    public bool HasIssues => UnexplainedSteps.Count > 0 || MissingSteps.Count > 0 || MagnitudeMismatches.Count > 0;
}
