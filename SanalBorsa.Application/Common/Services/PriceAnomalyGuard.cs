using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Enums;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Common.Services;

/// <summary>
/// Bir günde önceki kapanışa göre %20'den fazla sıçrayan/düşen bar'ları TV/Yahoo glitch ya da
/// kötü print kabul eder: o günün değerini yazmak yerine önceki günün kapanışını (flat) yazar ve
/// <see cref="AnomalyRecheckDelay"/> sonra kaynağı tekrar sormak üzere <see cref="IPriceAnomalyScheduler"/>
/// ile bir job zamanlar. Tekrar kontrolde hâlâ aynı anomaliyse önceki günün değeri kalıcı kalır;
/// farklıysa düzeltilir (bkz. Infrastructure/Jobs/PriceAnomalyRecheckJob.cs).
/// Piyasa-bağımsız — hem BIST hem ABD günlük fiyat senkronu tarafından kullanılır.
/// </summary>
public sealed class PriceAnomalyGuard
{
    private const decimal AnomalyLowerRatio = 0.8m;
    private const decimal AnomalyUpperRatio = 1.2m;

    private static readonly TimeSpan AnomalyRecheckDelay = TimeSpan.FromHours(6);

    private readonly IUnitOfWork _uow;
    private readonly IPriceAnomalyScheduler _anomalyScheduler;
    private readonly ILogger<PriceAnomalyGuard> _logger;

    public PriceAnomalyGuard(IUnitOfWork uow, IPriceAnomalyScheduler anomalyScheduler, ILogger<PriceAnomalyGuard> logger)
    {
        _uow = uow;
        _anomalyScheduler = anomalyScheduler;
        _logger = logger;
    }

    public async Task<List<StockPriceHistory>> SanitizeAsync(
        Stock stock,
        IReadOnlyList<StockPriceHistory> bars,
        CancellationToken ct)
    {
        var ordered = bars.OrderBy(b => b.Date).ToList();
        if (ordered.Count == 0) return ordered;

        var priorCloses = await _uow.PriceHistories.GetClosesOnOrBeforeAsync(
            [stock.Id], ordered[0].Date.Date.AddDays(-1), ct);
        decimal? prevClose = priorCloses.TryGetValue(stock.Id, out var pc) ? pc.Close : null;

        // Bilinen bir split (BonusIssue) gününde ham fiyat gerçekten %20+ sıçrar/düşer — bu bir
        // veri hatası değil, gerçek bir kurumsal olay. Böyle günleri anomali sayıp düzleştirmek,
        // hiçbir zaman "düzelmeyeceği" için placeholder'ı kalıcı hale getirir (split geri dönmez).
        // Bu yüzden zaten bilinen split tarihleri anomali kontrolünden muaf tutulur.
        var knownSplitDates = (await _uow.CorporateActions.GetByStockIdAndTypeAsync(
                stock.Id, CorporateActionType.BonusIssue, ct))
            .Select(a => a.ActionDate.Date)
            .ToHashSet();

        // İlk günün geriye bakacak bir prevClose'u yok (hissenin tüm geçmişinin en başı) — bu
        // yüzden normal döngü onu hiç anomali kontrolünden geçirmeden olduğu gibi yazar. Kaynağın
        // (TV/Yahoo) verdiği tek bir bozuk ilk print böylece sonsuza dek kalıcı kalır (bkz. AAPL
        // 1986-11-11 için gerçek ~$78 yerine $0.16 yazılması bug'ı). Bu yüzden ilk günü GERİYE değil
        // İLERİYE, bir sonraki güne göre kontrol ediyoruz.
        if (prevClose is null && ordered.Count > 1)
        {
            var first = ordered[0];
            var lookahead = ordered[1];
            if (lookahead.Close > 0 && !knownSplitDates.Contains(first.Date.Date) && IsAnomalous(lookahead.Close, first.Close))
            {
                _logger.LogWarning(
                    "Fiyat anomalisi (ilk gün): {Symbol} {Date:yyyy-MM-dd} close={Close} — sonraki gün ({NextDate:yyyy-MM-dd}) kapanışı {Next} ile değiştirildi.",
                    stock.Symbol, first.Date, first.Close, lookahead.Date, lookahead.Close);
                first.Open = first.High = first.Low = first.Close = first.AdjustedClose = lookahead.Close;
            }
        }

        var result = new List<StockPriceHistory>(ordered.Count);

        foreach (var bar in ordered)
        {
            if (prevClose is > 0 && !knownSplitDates.Contains(bar.Date.Date) && IsAnomalous(prevClose.Value, bar.Close))
            {
                var pctChange = (bar.Close / prevClose.Value - 1m) * 100m;
                _logger.LogWarning(
                    "Fiyat anomalisi: {Symbol} {Date:yyyy-MM-dd} close={Close} prevClose={Prev} ({Pct:0.0}%) — " +
                    "önceki günün kapanışı yazıldı, {Hours}s sonra tekrar kontrol edilecek.",
                    stock.Symbol, bar.Date, bar.Close, prevClose.Value, pctChange, AnomalyRecheckDelay.TotalHours);

                result.Add(new StockPriceHistory
                {
                    Date = bar.Date,
                    Open = prevClose.Value,
                    High = prevClose.Value,
                    Low = prevClose.Value,
                    Close = prevClose.Value,
                    AdjustedClose = prevClose.Value,
                    Volume = 0,
                });

                _anomalyScheduler.ScheduleRecheck(
                    stock.Symbol, bar.Date.Date, prevClose.Value, AnomalyRecheckDelay);

                // Yazılan satır placeholder (önceki gün) olsa da, sonraki günün karşılaştırma bazı
                // gerçekte GÖRÜLEN kapanış olmalı — yoksa kalıcı bir seviye değişimi (ör. gerçek bir
                // sermaye artırımı) takip eden HER günü sonsuza kadar "anomali" gösterir (domino etkisi).
                prevClose = bar.Close;
                continue;
            }

            result.Add(bar);
            prevClose = bar.Close;
        }

        return result;
    }

    private static bool IsAnomalous(decimal prevClose, decimal close)
    {
        var ratio = close / prevClose;
        return ratio < AnomalyLowerRatio || ratio > AnomalyUpperRatio;
    }
}
