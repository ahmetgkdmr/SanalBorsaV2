using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Enums;

namespace SanalBorsa.Infrastructure.ExternalServices.Kap;

public class KapCorporateActionService : IKapCorporateActionService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    private static readonly HashSet<string> RelevantSubjects = new(StringComparer.OrdinalIgnoreCase)
    {
        "Kar Payı Dağıtım İşlemlerine İlişkin Bildirim",
        "Sermaye Artırımı - Azaltımı İşlemlerine İlişkin Bildirim",
        "Hak Kullanımı"
    };

    /// <summary>
    /// KAP'ın şirket listesi SADECE GÜNCEL borsa kodunu tutuyor — şirket unvan/kod değiştirdiğinde
    /// (ör. tescil, isim değişikliği) eski kod listeden düşüyor, biz DB'de hâlâ eski BIST işlem
    /// sembolünü kullanıyoruz. Proje sohbeti: KOZAL (Koza Altın İşletmeleri) artık KAP'ta "TRALT"
    /// (Türk Altın İşletmeleri A.Ş.) olarak kayıtlı — bu yüzden KOZAL için OID hiç bulunamıyordu.
    /// Bilinen bu tarz değişiklikler için manuel bir eşleme; yenisi keşfedildikçe buraya eklenir.
    /// </summary>
    private static readonly Dictionary<string, string> KnownSymbolAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["KOZAL"] = "TRALT",
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<KapCorporateActionService> _logger;

    private Dictionary<string, string>? _symbolToOid;

    public KapCorporateActionService(
        IHttpClientFactory httpClientFactory,
        ILogger<KapCorporateActionService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CorporateAction>> GetCorporateActionsAsync(
        string bistSymbol,
        DateTime? sinceDate = null,
        CancellationToken ct = default)
    {
        var symbol = bistSymbol.Trim().ToUpperInvariant();
        var oid = await ResolveMemberOidAsync(symbol, ct);
        if (oid is null)
        {
            _logger.LogWarning("KAP member OID not found for {Symbol}", symbol);
            return [];
        }

        var client = _httpClientFactory.CreateClient("Kap");
        var startYear = sinceDate?.Year ?? 2010;
        var disclosures = await FetchRelevantDisclosuresAsync(client, oid, startYear, ct);
        var actions = new List<CorporateAction>();

        foreach (var disclosure in disclosures)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var (plain, html) = await FetchDisclosureContentAsync(client, disclosure.DisclosureIndex, ct);
                if (string.IsNullOrWhiteSpace(plain))
                    continue;

                actions.AddRange(ParseDisclosure(disclosure, plain, html, symbol));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to parse KAP disclosure {Index} for {Symbol}",
                    disclosure.DisclosureIndex, symbol);
            }

            await Task.Delay(250, ct);
        }

        var deduped = Deduplicate(actions);
        if (sinceDate is not null)
            deduped = deduped.Where(a => a.ActionDate.Date >= sinceDate.Value.Date).ToList();

        _logger.LogInformation(
            "KAP returned {Count} corporate actions for {Symbol} from {Disclosures} disclosures (since={Since})",
            deduped.Count, symbol, disclosures.Count,
            sinceDate?.ToString("yyyy-MM-dd") ?? "all");

        return deduped;
    }

    private async Task<string?> ResolveMemberOidAsync(string symbol, CancellationToken ct)
    {
        _symbolToOid ??= await LoadSymbolOidMapAsync(ct);
        if (_symbolToOid.TryGetValue(symbol, out var oid))
            return oid;

        if (KnownSymbolAliases.TryGetValue(symbol, out var alias)
            && _symbolToOid.TryGetValue(alias, out var aliasOid))
        {
            _logger.LogInformation("KAP: {Symbol} güncel kodda bulunamadı, bilinen takma ad {Alias} kullanıldı", symbol, alias);
            return aliasOid;
        }

        return null;
    }

    private async Task<Dictionary<string, string>> LoadSymbolOidMapAsync(CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("Kap");
        using var response = await SendWithRetryAsync(
            () => client.GetAsync("tr/api/company/items/IGS/A", ct),
            ct,
            maxAttempts: 6);
        response.EnsureSuccessStatusCode();

        var companies = await response.Content.ReadFromJsonAsync<List<KapCompanyItem>>(JsonOptions, ct)
                        ?? [];

        // KAP'ın "stockCode" alanı çoklu pay sınıfı/dual-listing olan şirketlerde TEK bir string
        // içinde virgülle ayrılmış birden fazla kod taşıyabiliyor (ör. "A1CAP, ACP",
        // "KRDMA, KRDMB, KRDMD", "ISMEN, IYM") — proje sohbeti: bunu tek bir sözlük anahtarı olarak
        // kaydedince ("A1CAP, ACP") "A1CAP" diye ayrı arayan hiçbir sorgu bulamıyordu. 751 şirketin
        // 45'inde bu durum var (94 alt-kod). Her alt-kodu AYRI bir anahtar olarak kaydediyoruz.
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in companies)
        {
            if (string.IsNullOrWhiteSpace(c.StockCode) || string.IsNullOrWhiteSpace(c.MkkMemberOid))
                continue;

            foreach (var rawCode in c.StockCode.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var code = rawCode.Trim().ToUpperInvariant();
                if (code.Length == 0) continue;
                map.TryAdd(code, c.MkkMemberOid); // ilk (en soldaki/birincil) kod kazanır
            }
        }

        return map;
    }

    private async Task<List<KapDisclosureItem>> FetchRelevantDisclosuresAsync(
        HttpClient client,
        string memberOid,
        int startYear,
        CancellationToken ct)
    {
        var results = new List<KapDisclosureItem>();
        var endYear = DateTime.UtcNow.Year;
        if (startYear < 2010)
            startYear = 2010;

        for (var year = startYear; year <= endYear; year++)
        {
            var payload = new
            {
                fromDate = $"{year}-01-01",
                toDate = $"{year}-12-31",
                mkkMemberOidList = new[] { memberOid },
                subjectList = Array.Empty<string>()
            };

            try
            {
                using var response = await SendWithRetryAsync(
                    () => client.PostAsJsonAsync(
                        "tr/api/disclosure/members/byCriteria", payload, ct),
                    ct);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "KAP byCriteria failed for year {Year}: {Status}",
                        year, response.StatusCode);
                    await Task.Delay(400, ct);
                    continue;
                }

                var batch = await response.Content.ReadFromJsonAsync<List<KapDisclosureItem>>(JsonOptions, ct)
                            ?? [];

                results.AddRange(batch.Where(IsRelevantDisclosure));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "KAP byCriteria exception for year {Year}", year);
            }

            await Task.Delay(250, ct);
        }

        return results
            .GroupBy(d => d.DisclosureIndex)
            .Select(g => g.First())
            .OrderBy(d => d.DisclosureIndex)
            .ToList();
    }

    private static bool IsRelevantDisclosure(KapDisclosureItem d)
    {
        var subject = d.Subject ?? string.Empty;
        if (RelevantSubjects.Contains(subject))
            return true;

        var summary = (d.Summary ?? string.Empty).ToLowerInvariant();
        return summary.Contains("bedelsiz")
               || summary.Contains("bedelli")
               || summary.Contains("sermaye artır")
               || summary.Contains("kar pay");
    }

    /// <summary>
    /// Hem düz metni (basit "içeriyor mu" kontrolleri ve eski regex fallback'leri için) hem HAM HTML'i
    /// (tablo yapısını anlayan yeni ayrıştırıcı için) döndürür — proje sohbeti: KAP bültenleri
    /// aslında düzenli HTML tablolar (başlık satırı + TOPLAM satırı, sütun sırasına göre hizalı);
    /// bunu düz metne çevirip "etiketten sonraki N karakter içinde sayı ara" gibi yakın-mesafe
    /// regex'lerle okumak kırılgan çıktı (AKFGY'de üç ayrı gerçek hata bulundu: Türkçe ek varyasyonu
    /// yüzünden etiket hiç bulunamaması, başlık ile TOPLAM satırı arası onlarca karakter olduğu için
    /// yakın-mesafe regex'in atlaması, ve en kötüsü yanlış sütunun sayısını yakalayıp %2611 gibi
    /// saçma bir değer üretmesi). Artık HTML tablo yapısını (satır/hücre) gerçekten ayrıştırıp
    /// SÜTUN BAŞLIĞI → aynı sütundaki TOPLAM değeri şeklinde okuyoruz — pozisyona değil, isme göre.
    /// </summary>
    private async Task<(string Plain, string Html)> FetchDisclosureContentAsync(
        HttpClient client,
        int disclosureIndex,
        CancellationToken ct)
    {
        using var response = await SendWithRetryAsync(
            () =>
            {
                var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"tr/api/notification/attachment-detail/{disclosureIndex}");
                request.Headers.TryAddWithoutValidation(
                    "Referer", $"https://www.kap.org.tr/tr/Bildirim/{disclosureIndex}");
                return client.SendAsync(request, ct);
            },
            ct);

        response.EnsureSuccessStatusCode();

        var details = await response.Content.ReadFromJsonAsync<List<KapDisclosureDetail>>(JsonOptions, ct);
        var bodies = details?.FirstOrDefault()?.DisclosureBody;
        if (bodies is null || bodies.Count == 0)
            return (string.Empty, string.Empty);

        var html = string.Join(" ", bodies.Where(b => !string.IsNullOrWhiteSpace(b)));
        var plain = CleanHtml(html);
        return (plain, html);
    }

    private static IEnumerable<CorporateAction> ParseDisclosure(
        KapDisclosureItem disclosure,
        string plain,
        string html,
        string symbol)
    {
        var subject = disclosure.Subject ?? string.Empty;
        var now = DateTime.UtcNow;

        if (subject.Contains("Sermaye Artırımı", StringComparison.OrdinalIgnoreCase)
            || plain.Contains("Bedelsiz Pay Alma", StringComparison.OrdinalIgnoreCase)
            || plain.Contains("Bedelli", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var action in ParseCapitalIncrease(plain, html, now))
                yield return action;
        }

        if (subject.Contains("Kar Payı", StringComparison.OrdinalIgnoreCase)
            || plain.Contains("Nakit Kar Payı", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var action in ParseDividend(plain, html, now))
                yield return action;
        }

        if (subject.Equals("Hak Kullanımı", StringComparison.OrdinalIgnoreCase)
            || plain.Contains("Bedelsiz Pay Alma Oranı (%)", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var action in ParseHakKullanimiBulletin(plain, now, symbol))
                yield return action;
        }
    }

    /// <summary>
    /// TEK yol: HTML'deki gerçek tablo yapısını (başlık satırı → aynı sütundaki özet/TOPLAM satırı
    /// değeri) SÜTUN İSMİNE göre okur, pozisyona/mesafeye göre DEĞİL — proje sohbeti: yakın-mesafe
    /// regex yaklaşımı ("etiketten sonraki N karakter içinde sayı ara") AKFGY VE THYAO'da gerçek,
    /// vahim hatalara yol açtı (ör. THYAO'nun bilinen ×1,15 bedelsizi yanlışlıkla ×12.000.000 olarak
    /// okunmuştu — yanlış sütunun mutlak TL tutarını yüzde sanmıştı). Bilinçli tasarım kararı: tablo
    /// yapısından güvenilir bir değer çıkaramıyorsak HİÇ değer üretmiyoruz (olay "eksik" sayılır,
    /// zaten audit araçlarımız bunu doğru şekilde yakalayıp bayraklıyor) — yanlış bir sayı üretip
    /// sessizce veriyi bozmaktan kesinlikle daha güvenli. "İç Kaynaklardan" ve "Kar Payından" gibi
    /// birden fazla bedelsiz kaynağı varsa TOPLAM'ı doğru toplar. KAP'ın en az iki farklı HTML
    /// şablon nesli (2013: "ŞİRKET BAZINDA BİLGİLER" özet satırı; 2021+: "TOPLAM") aynı kodla okunur
    /// — bkz. ExtractTotalRowColumnMaps.
    /// </summary>
    private static IEnumerable<CorporateAction> ParseCapitalIncrease(string plain, string html, DateTime now)
    {
        var labelValues = ExtractLabelValuePairs(html);
        var totalTables = ExtractTotalRowColumnMaps(html);

        var usageDate = FuzzyFindDate(labelValues, "Kullan", "Başlangıç", "Tarih")
            ?? FuzzyFindDate(labelValues, "Kesinleşen");

        decimal? bedelsizPct = null;
        decimal? bedelliPct = null;

        foreach (var table in totalTables)
        {
            if (bedelsizPct is null)
            {
                var icKaynak = FuzzyFindDecimal(table, "İç Kaynak", "Oranı") ?? 0m;
                var karPayi = FuzzyFindDecimal(table, "Kar Pay", "Bedelsiz", "Oranı") ?? 0m;
                var toplam = icKaynak + karPayi;
                if (toplam <= 0m)
                    toplam = FuzzyFindDecimal(table, "Bedelsiz", "Oranı") ?? 0m; // tek-sütunlu eski format
                if (toplam > 0m)
                    bedelsizPct = toplam;
            }

            if (bedelliPct is null)
            {
                var ruchan = FuzzyFindDecimal(table, "Rüçhan", "Oranı");
                if (ruchan is > 0m)
                    bedelliPct = ruchan;
            }
        }

        // En eski (2010 dönemi) KAP bültenleri tablo sütunu değil, düz "Etiket : Değer" satırı
        // kullanıyor (ör. "Bedelsiz Artırım Oranı (%) : 154,17") — proje sohbeti: KOZAL/TRALT'ın
        // 2010-06-16 bedelsizi bu formattaydı, TOPLAM tablosu hiç yoktu. labelValues'tan da dene.
        bedelsizPct ??= FuzzyFindDecimal(labelValues, "Bedelsiz", "Artırım", "Oranı")
            ?? FuzzyFindDecimal(labelValues, "Bedelsiz", "Pay Alma", "Oranı");
        bedelliPct ??= FuzzyFindDecimal(labelValues, "Bedelli", "Oranı")
            ?? FuzzyFindDecimal(labelValues, "Rüçhan", "Oranı");

        // Fiyat özet satırında değil, ana (grup bazlı) tabloda bir sütun — her grup satırı aynı
        // fiyatı taşıyor, o yüzden ilk veri satırından okumak yeterli.
        var subscriptionPrice = ExtractFirstColumnValueAfterHeader(html, "Rüçhan Hakkı Kullandırma Fiyatı")
            ?? ExtractFirstColumnValueAfterHeader(html, "Rüçhan Hakkı Kullanım Fiyatı")
            ?? FuzzyFindDecimal(labelValues, "Rüçhan", "Kullandırma", "Fiyat")
            ?? FuzzyFindDecimal(labelValues, "Rüçhan", "Kullanım", "Fiyat");

        if (usageDate is null)
            yield break;

        if (bedelsizPct is > 0m)
        {
            var multiplier = Math.Round(1m + bedelsizPct.Value / 100m, 8);
            yield return new CorporateAction
            {
                ActionType = CorporateActionType.BonusIssue,
                ActionDate = usageDate.Value,
                Value = multiplier,
                Description =
                    $"KAP bedelsiz: %{FormatPct(bedelsizPct.Value)} (×{multiplier.ToString("0.####", CultureInfo.InvariantCulture)})",
                CreatedAt = now
            };
        }

        if (bedelliPct is > 0m)
        {
            var ratio = Math.Round(bedelliPct.Value / 100m, 8);
            var priceNote = subscriptionPrice is > 0m
                ? $" @ {subscriptionPrice.Value.ToString("0.####", CultureInfo.InvariantCulture)} TL"
                : string.Empty;

            yield return new CorporateAction
            {
                ActionType = CorporateActionType.RightsIssue,
                ActionDate = usageDate.Value,
                Value = ratio,
                SubscriptionPrice = subscriptionPrice is > 0m ? Math.Round(subscriptionPrice.Value, 6) : null,
                Description = $"KAP bedelli: %{FormatPct(bedelliPct.Value)}{priceNote}",
                CreatedAt = now
            };
        }
    }

    private static IEnumerable<CorporateAction> ParseDividend(string plain, string html, DateTime now)
    {
        if (plain.Contains("Nakit Kar Payı Ödeme Şekli Ödenmeyecek", StringComparison.OrdinalIgnoreCase))
            yield break;

        if (plain.Contains("dağıtılabilir kârının oluşmadığı", StringComparison.OrdinalIgnoreCase)
            || plain.Contains("dağıtılabilir kar oluşmadığı", StringComparison.OrdinalIgnoreCase))
            yield break;

        var labelValues = ExtractLabelValuePairs(html);

        var perShare = FuzzyFindDecimal(labelValues, "1 TL", "Nakit Kar Payı", "Brüt")
            ?? ExtractTrDecimalAfter(
                plain,
                @"1 TL Nominal Değerli Paya Ödenecek Nakit Kar Payı - Brüt\(TL\)[^0-9]{0,80}?(\d+(?:[.,]\d+)?)");

        if (perShare is null or <= 0m)
        {
            var m = Regex.Match(
                plain,
                @"TRE[A-Z0-9]+[^0-9]{0,20}(\d+,\d+)\s+(\d+(?:,\d+)?)",
                RegexOptions.IgnoreCase);
            if (m.Success)
                perShare = ParseTrDecimal(m.Groups[1].Value);
        }

        if (perShare is null or <= 0m)
            yield break;

        var date = FuzzyFindDate(labelValues, "Kesinleşen", "Kar Payı", "Kullan", "Tarih")
            ?? FuzzyFindDate(labelValues, "Kar Payı", "Kullan", "Tarih")
            ?? ExtractDateAfterLabel(plain, "Kesinleşen Nakit Kar Payı Hak Kullanım Tarihi")
            ?? ExtractDateAfterLabel(plain, "Teklif Edilen Nakit Kar Payı Hak Kullanım Tarihi")
            ?? ExtractDateAfterLabel(plain, "Hak Kullanım Tarihi");

        if (date is null)
            yield break;

        yield return new CorporateAction
        {
            ActionType = CorporateActionType.Dividend,
            ActionDate = date.Value,
            Value = Math.Round(perShare.Value, 8),
            Description =
                $"KAP nakit temettü: {perShare.Value.ToString("0.####", CultureInfo.InvariantCulture)} TL/hisse",
            CreatedAt = now
        };
    }

    private static IEnumerable<CorporateAction> ParseHakKullanimiBulletin(
        string plain,
        DateTime now,
        string symbol)
    {
        var dateMatch = Regex.Match(plain, @"(\d{2}\.\d{2}\.\d{4})\s+tarihinden itibaren");
        if (!dateMatch.Success || !TryParseTrDate(dateMatch.Groups[1].Value, out var date))
            yield break;

        // Prefer SYMBOL + decimal-comma ratio; avoid SYMBOL + date (dd.MM.yyyy)
        var escaped = Regex.Escape(symbol);
        var ratioMatch = Regex.Match(
            plain,
            $@"{escaped}\s+(\d{{1,3}}(?:\.\d{{3}})*,\d+)",
            RegexOptions.IgnoreCase);
        if (!ratioMatch.Success)
            yield break;

        var pct = ParseTrDecimal(ratioMatch.Groups[1].Value);
        if (pct <= 0m)
            yield break;

        var multiplier = Math.Round(1m + pct / 100m, 8);
        yield return new CorporateAction
        {
            ActionType = CorporateActionType.BonusIssue,
            ActionDate = date,
            Value = multiplier,
            Description =
                $"KAP hak kullanımı: %{FormatPct(pct)} (×{multiplier.ToString("0.####", CultureInfo.InvariantCulture)})",
            CreatedAt = now
        };
    }

    // ---- HTML tablo-farkında ayrıştırma yardımcıları -------------------------------------------
    // KAP bültenleri düzenli HTML tablolar kullanıyor: basit alanlar
    // <td><div class="bold font14">ETİKET</div></td><td><div class="gwt-HTML control-label...">DEĞER</div></td>
    // ikilisi; toplam/oran alanları ise "totalTableStyle" sınıflı bir tabloda BAŞLIK SATIRI +
    // "TOPLAM" ile başlayan veri satırı şeklinde, sütun sırasına göre hizalı. Regex ile "etiketten
    // sonraki N karakter içinde sayı ara" yaklaşımı üç ayrı gerçek hataya yol açmıştı (bkz. proje
    // sohbeti, AKFGY): burada onun yerine gerçek satır/hücre yapısını okuyup İSME göre eşliyoruz.

    // KAP en az İKİ farklı HTML şablon nesli kullanıyor — proje sohbeti: 2013 (THYAO) bültenleri
    // düz "tablohucreb" sınıflı <td> hücreleri ve "ŞİRKET BAZINDA BİLGİLER" özet satırı kullanırken,
    // 2021+ (AKFGY) bültenleri "gwt-HTML control-label"/"bold font14" iç div'leri ve "TOPLAM" özet
    // satırı kullanıyor. Aşağıdaki yardımcılar İKİSİNİ DE tanır: hücre metnini iç yapıya bakmadan
    // (hangi div/class olursa olsun) çıkarır, tablo sınıfına göre değil İÇERİĞİNE göre (başlıkta
    // "Oranı"/"(%)" geçen bir tablo mu) filtreler, özet satırını "TOPLAM" veya "ŞİRKET BAZINDA"
    // ile başlayan satır olarak tanır.
    private static readonly Regex TableRx = new(@"<table\b[^>]*>(.*?)</table>", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex RowRx = new(@"<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex CellRx = new(@"<td[^>]*>(.*?)</td>", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly string[] SummaryRowPrefixes = ["TOPLAM", "ŞİRKET BAZINDA"];

    private static List<string> ExtractCells(string rowHtml) =>
        CellRx.Matches(rowHtml).Select(c => CleanHtml(c.Groups[1].Value)).ToList();

    private static bool IsSummaryRow(IReadOnlyList<string> cells) =>
        cells.Count > 0 && SummaryRowPrefixes.Any(p =>
            cells[0].StartsWith(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Basit "etiket: değer" satırlarını, aradaki iç HTML yapısına (div class'ı ne olursa olsun)
    /// bakmadan, sadece "2 hücreli, tek başlık sütunu görünen satır" örüntüsünden çıkarır — hem eski
    /// hem yeni KAP şablonuyla çalışır.
    /// </summary>
    private static Dictionary<string, string> ExtractLabelValuePairs(string html)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match rowMatch in RowRx.Matches(html))
        {
            var cells = ExtractCells(rowMatch.Groups[1].Value);

            // 2013+ format: <td>ETİKET</td><td>DEĞER</td>
            // 2010-dönemi eski format: <td>ETİKET</td><td>:</td><td>DEĞER</td> — ayraç kendi hücresinde.
            string label, value;
            if (cells.Count == 2)
            {
                label = cells[0];
                value = cells[1];
            }
            else if (cells.Count == 3 && cells[1] == ":")
            {
                label = cells[0];
                value = cells[2];
            }
            else
            {
                continue;
            }

            if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(value) || label.Length > 120)
                continue;
            if (!map.ContainsKey(label))
                map[label] = value;
        }
        return map;
    }

    /// <summary>
    /// Başlık satırında en az bir "Oranı" (ör. "... Oranı (%)") sütunu geçen HER tabloyu bulur,
    /// o tablonun ÖZET satırındaki (ilk hücresi "TOPLAM" veya "ŞİRKET BAZINDA" ile başlayan) her
    /// değeri, AYNI SÜTUNUN başlık ismiyle eşleyerek döndürür. Tablo sınıfına bakmaz (KAP şablonu
    /// yıllar içinde değişmiş), sadece içeriğine (oran sütunu var mı) ve yapısına (özet satırı var
    /// mı) bakar — bu yüzden hem 2013 hem 2021 formatı aynı kodla doğru okunuyor.
    /// Üçüncü bir varyant: tek pay grubu olan şirketlerde (ör. ADEL) birden fazla grup satırı
    /// olmadığı için hiç "TOPLAM" satırı YOK — tablo sadece başlık + TEK veri satırından oluşuyor.
    /// Bu durumda o tek satırın kendisi zaten toplamı temsil ediyor, onu doğrudan kullanıyoruz.
    /// </summary>
    private static List<Dictionary<string, string>> ExtractTotalRowColumnMaps(string html)
    {
        var results = new List<Dictionary<string, string>>();

        foreach (Match tableMatch in TableRx.Matches(html))
        {
            var rows = RowRx.Matches(tableMatch.Groups[1].Value);
            if (rows.Count < 2) continue;

            var header = ExtractCells(rows[0].Groups[1].Value);
            if (!header.Any(h => h.Contains("Oranı", StringComparison.OrdinalIgnoreCase)))
                continue; // bu tablo bir oran/yüzde tablosu değil, atla (adres/imza tabloları vb.)

            Dictionary<string, string>? found = null;
            for (var i = 1; i < rows.Count; i++)
            {
                var cells = ExtractCells(rows[i].Groups[1].Value);
                if (!IsSummaryRow(cells)) continue;

                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var c = 0; c < header.Count && c < cells.Count; c++)
                {
                    if (!string.IsNullOrWhiteSpace(header[c]))
                        map[header[c]] = cells[c];
                }
                found = map;
                break;
            }

            if (found is null && rows.Count == 2)
            {
                var cells = ExtractCells(rows[1].Groups[1].Value);
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var c = 0; c < header.Count && c < cells.Count; c++)
                {
                    if (!string.IsNullOrWhiteSpace(header[c]))
                        map[header[c]] = cells[c];
                }
                found = map;
            }

            if (found is not null)
                results.Add(found);
        }

        return results;
    }

    /// <summary>Başlık satırında verilen sütun adını içeren İLK tabloyu bulup, o sütunun İLK VERİ
    /// satırındaki değerini döndürür (özet satırında olmayan, sadece grup bazlı ana tabloda bulunan
    /// alanlar için — ör. Rüçhan Hakkı Kullandırma Fiyatı).</summary>
    private static decimal? ExtractFirstColumnValueAfterHeader(string html, string columnNameContains)
    {
        foreach (Match tableMatch in TableRx.Matches(html))
        {
            var rows = RowRx.Matches(tableMatch.Groups[1].Value);
            if (rows.Count < 2) continue;

            var header = ExtractCells(rows[0].Groups[1].Value);
            var colIdx = header.FindIndex(h => h.Contains(columnNameContains, StringComparison.OrdinalIgnoreCase));
            if (colIdx < 0) continue;

            for (var i = 1; i < rows.Count; i++)
            {
                var cells = ExtractCells(rows[i].Groups[1].Value);
                if (colIdx < cells.Count)
                {
                    var v = ParseTrDecimalOrNull(cells[colIdx]);
                    if (v is > 0m) return v;
                }
            }
        }
        return null;
    }

    /// <summary>Verilen anahtar sözcüklerin TAMAMINI içeren ilk etiketin değerini (ondalık olarak)
    /// döndürür — Türkçe ek varyasyonlarına (ör. "Kullanım" vs "Kullanımı") karşı bağışıklı, çünkü
    /// tam etiket metnine değil KÖK sözcüklere bakıyor.</summary>
    private static decimal? FuzzyFindDecimal(Dictionary<string, string> map, params string[] mustContainAll)
    {
        foreach (var kv in map)
        {
            if (mustContainAll.All(k => kv.Key.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                var v = ParseTrDecimalOrNull(kv.Value);
                if (v is not null) return v;
            }
        }
        return null;
    }

    private static DateTime? FuzzyFindDate(Dictionary<string, string> map, params string[] mustContainAll)
    {
        foreach (var kv in map)
        {
            if (mustContainAll.All(k => kv.Key.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                var m = Regex.Match(kv.Value, @"\d{2}\.\d{2}\.\d{4}");
                if (m.Success && TryParseTrDate(m.Value, out var d))
                    return d;
            }
        }
        return null;
    }

    private static decimal? ParseTrDecimalOrNull(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try { return ParseTrDecimal(raw); } catch { return null; }
    }

    private static string CleanHtml(string raw)
    {
        var text = Regex.Replace(raw, "<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static DateTime? ExtractDateAfterLabel(string plain, string label)
    {
        var idx = plain.IndexOf(label, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;

        var window = plain.Substring(idx, Math.Min(120, plain.Length - idx));
        var m = Regex.Match(window, @"\d{2}\.\d{2}\.\d{4}");
        if (!m.Success || !TryParseTrDate(m.Value, out var date))
            return null;

        return date;
    }

    private static decimal? ExtractTrDecimalAfter(string plain, string pattern)
    {
        var m = Regex.Match(plain, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return m.Success ? ParseTrDecimal(m.Groups[1].Value) : null;
    }

    private static decimal ParseTrDecimal(string raw)
    {
        var cleaned = raw.Trim();
        if (cleaned.Contains(','))
        {
            cleaned = cleaned.Replace(".", "").Replace(',', '.');
        }
        else if (cleaned.Count(c => c == '.') > 1)
        {
            cleaned = cleaned.Replace(".", "");
        }

        return decimal.Parse(cleaned, CultureInfo.InvariantCulture);
    }

    private static bool TryParseTrDate(string raw, out DateTime date)
        => DateTime.TryParseExact(raw, "dd.MM.yyyy", Tr, DateTimeStyles.None, out date);

    private static string FormatPct(decimal pct)
        => pct.ToString("0.##", CultureInfo.InvariantCulture);

    private static List<CorporateAction> Deduplicate(IEnumerable<CorporateAction> actions)
        => actions
            .GroupBy(a => (a.ActionDate.Date, a.ActionType))
            .Select(g => g
                .OrderByDescending(a => a.Value)
                .ThenByDescending(a => a.SubscriptionPrice ?? 0m)
                .First())
            .OrderBy(a => a.ActionDate)
            .ThenBy(a => a.ActionType)
            .ToList();

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<Task<HttpResponseMessage>> send,
        CancellationToken ct,
        int maxAttempts = 4)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var response = await send();
                if ((int)response.StatusCode is 429 or >= 500)
                {
                    var status = (int)response.StatusCode;
                    var delayMs = status == 429
                        ? 15_000 * attempt
                        : 500 * attempt * attempt;
                    response.Dispose();
                    _logger.LogWarning(
                        "KAP HTTP {Status} — backing off {Delay}ms (attempt {Attempt}/{Max})",
                        status, delayMs, attempt, maxAttempts);
                    await Task.Delay(delayMs, ct);
                    continue;
                }

                return response;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                last = ex;
                _logger.LogWarning(
                    "KAP HTTP attempt {Attempt}/{Max} failed: {Message}",
                    attempt, maxAttempts, ex.Message);
                await Task.Delay(700 * attempt * attempt, ct);
            }
        }

        throw last ?? new HttpRequestException("KAP request failed after retries");
    }

    private sealed class KapCompanyItem
    {
        public string? StockCode { get; set; }
        public string? MkkMemberOid { get; set; }
    }

    private sealed class KapDisclosureItem
    {
        public int DisclosureIndex { get; set; }
        public string? Subject { get; set; }
        public string? Summary { get; set; }
        public string? PublishDate { get; set; }
    }

    private sealed class KapDisclosureDetail
    {
        public List<string>? DisclosureBody { get; set; }
    }
}
