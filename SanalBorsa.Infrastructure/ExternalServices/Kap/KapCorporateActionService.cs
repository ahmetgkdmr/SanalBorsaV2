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
                var plain = await FetchDisclosurePlainTextAsync(client, disclosure.DisclosureIndex, ct);
                if (string.IsNullOrWhiteSpace(plain))
                    continue;

                actions.AddRange(ParseDisclosure(disclosure, plain, symbol));
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
        return _symbolToOid.TryGetValue(symbol, out var oid) ? oid : null;
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

        return companies
            .Where(c => !string.IsNullOrWhiteSpace(c.StockCode) && !string.IsNullOrWhiteSpace(c.MkkMemberOid))
            .GroupBy(c => c.StockCode!.Trim().ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First().MkkMemberOid!);
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

    private async Task<string> FetchDisclosurePlainTextAsync(
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
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var html in bodies)
        {
            if (string.IsNullOrWhiteSpace(html))
                continue;

            var text = Regex.Replace(html, "<[^>]+>", " ");
            text = System.Net.WebUtility.HtmlDecode(text);
            text = Regex.Replace(text, @"\s+", " ").Trim();
            sb.Append(text).Append(' ');
        }

        return sb.ToString();
    }

    private static IEnumerable<CorporateAction> ParseDisclosure(
        KapDisclosureItem disclosure,
        string plain,
        string symbol)
    {
        var subject = disclosure.Subject ?? string.Empty;
        var now = DateTime.UtcNow;

        if (subject.Contains("Sermaye Artırımı", StringComparison.OrdinalIgnoreCase)
            || plain.Contains("Bedelsiz Pay Alma", StringComparison.OrdinalIgnoreCase)
            || plain.Contains("Bedelli", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var action in ParseCapitalIncrease(plain, now))
                yield return action;
        }

        if (subject.Contains("Kar Payı", StringComparison.OrdinalIgnoreCase)
            || plain.Contains("Nakit Kar Payı", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var action in ParseDividend(plain, now))
                yield return action;
        }

        if (subject.Equals("Hak Kullanımı", StringComparison.OrdinalIgnoreCase)
            || plain.Contains("Bedelsiz Pay Alma Oranı (%)", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var action in ParseHakKullanimiBulletin(plain, now, symbol))
                yield return action;
        }
    }

    private static IEnumerable<CorporateAction> ParseCapitalIncrease(string plain, DateTime now)
    {
        var usageDate =
            ExtractDateAfterLabel(plain, "Bedelsiz Pay Alma Hakkı Kullanım Başlangıç Tarihi")
            ?? ExtractDateAfterLabel(plain, "Hak Kullanım Başlangıç Tarihi")
            ?? ExtractDateAfterLabel(plain, "Kesinleşen");

        var bedelsizPct =
            ExtractTrDecimalAfter(plain,
                @"TOPLAM\s+\d{1,3}(?:\.\d{3})*\s+\d{1,3}(?:\.\d{3})*(?:,\d+)?\s+(\d{1,3}(?:\.\d{3})*,\d+)")
            ?? ExtractTrDecimalAfter(plain,
                @"İç Kaynaklardan Bedelsiz Pay Alma Oranı\s*\(%\)(?:(?!İç Kaynaklardan Bedelsiz Pay Alma Oranı).){0,400}?(\d{1,3}(?:\.\d{3})*,\d+)")
            ?? ExtractFirstBedelsizPct(plain);

        var bedelliPct = ExtractTrDecimalAfter(
            plain,
            @"Rüçhan Hakkı Kullanımı Oranı\s*\(%\)\s*[^0-9]{0,40}?(\d{1,3}(?:\.\d{3})*(?:,\d+)?)");

        var subscriptionPrice =
            ExtractTrDecimalAfter(plain,
                @"Rüçhan Hakkı Kullandırma Fiyatı\s*\(TL\)\s*[^0-9]{0,40}?(\d+(?:[.,]\d+)?)")
            ?? ExtractTrDecimalAfter(plain,
                @"Rüçhan Hakkı Kullanım Fiyatı\s*\(TL\)\s*[^0-9]{0,40}?(\d+(?:[.,]\d+)?)")
            ?? ExtractTrDecimalAfter(plain,
                @"Rüçhan Hakkı Kullandırma Fiyatı[^0-9]{0,40}?(\d+(?:[.,]\d+)?)");

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

    private static IEnumerable<CorporateAction> ParseDividend(string plain, DateTime now)
    {
        if (plain.Contains("Nakit Kar Payı Ödeme Şekli Ödenmeyecek", StringComparison.OrdinalIgnoreCase))
            yield break;

        if (plain.Contains("dağıtılabilir kârının oluşmadığı", StringComparison.OrdinalIgnoreCase)
            || plain.Contains("dağıtılabilir kar oluşmadığı", StringComparison.OrdinalIgnoreCase))
            yield break;

        var perShare = ExtractTrDecimalAfter(
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

        var date =
            ExtractDateAfterLabel(plain, "Kesinleşen Nakit Kar Payı Hak Kullanım Tarihi")
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

    private static decimal? ExtractFirstBedelsizPct(string plain)
    {
        var m = Regex.Match(
            plain,
            @"Bedelsiz Pay Alma Oranı\s*\(%\)(?:(?!Bedelsiz Pay Alma Oranı).){0,400}?(\d{1,3}(?:\.\d{3})*,\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return m.Success ? ParseTrDecimal(m.Groups[1].Value) : null;
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
