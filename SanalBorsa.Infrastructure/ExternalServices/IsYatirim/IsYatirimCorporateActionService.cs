using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Enums;
using SanalBorsa.Infrastructure.ExternalServices.IsYatirim.Models;

namespace SanalBorsa.Infrastructure.ExternalServices.IsYatirim;

public class IsYatirimCorporateActionService : IIsYatirimCorporateActionService
{
    private const string Endpoint =
        "_layouts/15/IsYatirim.Website/StockInfo/CompanyInfoAjax.aspx/GetSermayeArttirimlari";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly TimeZoneInfo TurkeyTz = ResolveTurkeyTimeZone();

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<IsYatirimCorporateActionService> _logger;

    public IsYatirimCorporateActionService(
        IHttpClientFactory httpClientFactory,
        ILogger<IsYatirimCorporateActionService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CorporateAction>> GetCorporateActionsAsync(
        string bistSymbol,
        CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient("IsYatirim");
        var payload = JsonSerializer.Serialize(new
        {
            hisseKodu = bistSymbol,
            hisseTanimKodu = "",
            yil = 0,
            zaman = "HEPSI",
            endeksKodu = "09",
            sektorKodu = ""
        });

        try
        {
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(Endpoint, content, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var envelope = JsonSerializer.Deserialize<IsYatirimWebMethodResponse>(json, JsonOptions);
            if (string.IsNullOrWhiteSpace(envelope?.D))
            {
                _logger.LogWarning("Empty İş Yatırım corporate-action payload for {Symbol}", bistSymbol);
                return [];
            }

            var rows = JsonSerializer.Deserialize<List<IsYatirimSermayeArttirimRow>>(envelope.D, JsonOptions);
            if (rows is null || rows.Count == 0)
                return [];

            var actions = new List<CorporateAction>();
            foreach (var row in rows)
            {
                if (row.TarihEpochMs <= 0)
                    continue;

                var date = EpochMsToTurkeyDate(row.TarihEpochMs);
                MapRow(row, date, actions);
            }

            _logger.LogInformation(
                "İş Yatırım returned {Count} corporate actions for {Symbol} from {Rows} rows",
                actions.Count, bistSymbol, rows.Count);

            return actions
                .OrderBy(a => a.ActionDate)
                .ThenBy(a => a.ActionType)
                .ToList();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error fetching İş Yatırım corporate actions for {Symbol}", bistSymbol);
            return [];
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON parse error for İş Yatırım corporate actions of {Symbol}", bistSymbol);
            return [];
        }
    }

    /// <summary>
    /// One API row can produce multiple typed actions (e.g. bedelli + bedelsiz same day).
    /// Values follow TimeMachineCalculator conventions:
    /// - Dividend: TRY per share (Hisse Başı Brüt = Nakit Tem. Brüt % / 100)
    /// - BonusIssue: total lot multiplier = 1 + (Bedelsiz IK% + Bedelsiz Temettü%) / 100
    /// - RightsIssue: new/old ratio = Bedelli Oran% / 100
    /// </summary>
    private static void MapRow(IsYatirimSermayeArttirimRow row, DateTime date, List<CorporateAction> actions)
    {
        var tip = row.TipTanimi?.Trim();
        var now = DateTime.UtcNow;

        if (row.BedelliOranPct > 0m)
        {
            var ratio = Math.Round(row.BedelliOranPct / 100m, 8);
            actions.Add(new CorporateAction
            {
                ActionType = CorporateActionType.RightsIssue,
                ActionDate = date,
                Value = ratio,
                SubscriptionPrice = row.BedelliNomTutar > 0
                    ? Math.Round(row.BedelliNomTutar, 6)
                    : null,
                Description = BuildDescription(
                    tip ?? "Bedelli sermaye artırımı",
                    $"%{FormatPct(row.BedelliOranPct)}" +
                    (row.BedelliNomTutar > 0
                        ? $", nom. {row.BedelliNomTutar.ToString("0.####", CultureInfo.InvariantCulture)} TL"
                        : "")),
                CreatedAt = now
            });
        }

        var bedelsizPct = row.BedelsizIkOranPct + row.BedelsizTemettuOranPct;
        if (bedelsizPct > 0m)
        {
            var multiplier = Math.Round(1m + bedelsizPct / 100m, 8);
            actions.Add(new CorporateAction
            {
                ActionType = CorporateActionType.BonusIssue,
                ActionDate = date,
                Value = multiplier,
                Description = BuildDescription(
                    tip ?? "Bedelsiz sermaye artırımı",
                    $"%{FormatPct(bedelsizPct)} (×{multiplier.ToString("0.####", CultureInfo.InvariantCulture)})"),
                CreatedAt = now
            });
        }

        if (row.NakitTemettuOranPct > 0m || row.NakitTemettuTutar > 0m)
        {
            // UI "Hisse Başı Brüt (TL)" = Nakit Tem. Brüt (%) / 100
            var perShare = Math.Round(row.NakitTemettuOranPct / 100m, 8);
            if (perShare <= 0m)
                return;

            actions.Add(new CorporateAction
            {
                ActionType = CorporateActionType.Dividend,
                ActionDate = date,
                Value = perShare,
                Description = BuildDescription(
                    tip ?? "Nakit temettü",
                    $"{perShare.ToString("0.####", CultureInfo.InvariantCulture)} TL/hisse"),
                CreatedAt = now
            });
        }
    }

    private static string BuildDescription(string tip, string detail)
        => string.IsNullOrWhiteSpace(detail) ? tip : $"{tip}: {detail}";

    private static string FormatPct(decimal pct)
        => pct.ToString("0.##", CultureInfo.InvariantCulture);

    private static DateTime EpochMsToTurkeyDate(long epochMs)
    {
        var utc = DateTimeOffset.FromUnixTimeMilliseconds(epochMs);
        return TimeZoneInfo.ConvertTime(utc, TurkeyTz).Date;
    }

    private static TimeZoneInfo ResolveTurkeyTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows() ? "Turkey Standard Time" : "Europe/Istanbul");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
