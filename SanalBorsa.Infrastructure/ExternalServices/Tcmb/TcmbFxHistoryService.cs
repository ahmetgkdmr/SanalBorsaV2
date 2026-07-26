using System.Globalization;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Application.Common.Seeds;
using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Infrastructure.ExternalServices.Tcmb;

/// <summary>
/// TCMB <c>kurlar/YYYYMM/DDMMYYYY.xml</c> — API key yok.
/// Tek HTTP turunda USD / EUR / XAU okunur.
/// </summary>
public sealed class TcmbFxHistoryService : ITcmbFxHistoryService
{
    private static readonly DateTime YeniTlCutover = new(2005, 1, 1);
    private static readonly DateTime ArchiveFloor = new(1999, 1, 4);
    private const decimal OldTlDivisor = 1_000_000m;
    private const decimal DemPerEuro = 1.95583m;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TcmbFxHistoryService> _logger;

    public TcmbFxHistoryService(
        IHttpClientFactory httpClientFactory,
        ILogger<TcmbFxHistoryService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<TcmbFxBundle> GetParityBundleAsync(
        DateTime from,
        DateTime to,
        CancellationToken ct = default)
    {
        var fromDate = from.Date < ArchiveFloor ? ArchiveFloor : from.Date;
        var toDate = to.Date;
        if (fromDate > toDate)
            return new TcmbFxBundle([], [], []);

        var client = _httpClientFactory.CreateClient("Tcmb");
        var usd = new SortedDictionary<DateTime, decimal>();
        var eur = new SortedDictionary<DateTime, decimal>();
        var goldGram = new SortedDictionary<DateTime, decimal>();

        var dates = new List<DateTime>();
        for (var d = fromDate; d <= toDate; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            dates.Add(d);
        }

        const int parallelism = 10;
        var ok = 0;
        for (var i = 0; i < dates.Count; i += parallelism)
        {
            ct.ThrowIfCancellationRequested();
            var batch = dates.Skip(i).Take(parallelism).ToList();
            var results = await Task.WhenAll(batch.Select(d => FetchDayAsync(client, d, ct)));

            foreach (var day in results)
            {
                if (day is null) continue;
                ok++;

                if (day.Usd is { } u) usd[day.Date] = u;

                if (day.Eur is { } e)
                    eur[day.Date] = e;
                else if (day.Dem is { } dem)
                    eur[day.Date] = dem / DemPerEuro;

                if (day.XauOnzTry is { } xau)
                    goldGram[day.Date] = xau / MarketInstrumentSeed.GramsPerTroyOunce;
            }
        }

        _logger.LogInformation(
            "TCMB bundle: {Ok}/{Total} gün XML — USD={Usd} EUR={Eur} Gold={Gold}",
            ok, dates.Count, usd.Count, eur.Count, goldGram.Count);

        return new TcmbFxBundle(
            ToBars(usd, 4),
            ToBars(eur, 4),
            ToBars(goldGram, 2));
    }

    private static List<StockPriceHistory> ToBars(SortedDictionary<DateTime, decimal> map, int decimals)
    {
        var now = DateTime.UtcNow;
        return map.Select(kv =>
        {
            var c = Math.Round(kv.Value, decimals);
            return new StockPriceHistory
            {
                Date = kv.Key,
                Open = c,
                High = c,
                Low = c,
                Close = c,
                AdjustedClose = c,
                Volume = 0,
                CreatedAt = now,
            };
        }).ToList();
    }

    private async Task<TcmbDayRates?> FetchDayAsync(HttpClient client, DateTime date, CancellationToken ct)
    {
        var path = $"kurlar/{date:yyyyMM}/{date:ddMMyyyy}.xml";
        try
        {
            using var resp = await client.GetAsync(path, ct);
            if (!resp.IsSuccessStatusCode)
                return null;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            var doc = await XDocument.LoadAsync(stream, LoadOptions.None, ct);
            var root = doc.Root;
            if (root is null) return null;

            decimal? usd = null, eur = null, dem = null, xau = null;

            foreach (var cur in root.Elements().Where(e => e.Name.LocalName == "Currency"))
            {
                var code = (string?)cur.Attribute("CurrencyCode")
                           ?? (string?)cur.Attribute("Kod");
                if (string.IsNullOrWhiteSpace(code))
                    continue;

                var buying = cur.Elements().FirstOrDefault(e => e.Name.LocalName == "ForexBuying")?.Value
                             ?? cur.Elements().FirstOrDefault(e => e.Name.LocalName == "BanknoteBuying")?.Value;
                if (!TryParseTr(buying, out var raw) || raw <= 0)
                    continue;

                var unitStr = (string?)cur.Attribute("Unit") ?? "1";
                _ = int.TryParse(unitStr, out var unit);
                if (unit <= 0) unit = 1;
                var perUnit = raw / unit;
                if (date < YeniTlCutover)
                    perUnit /= OldTlDivisor;

                switch (code.ToUpperInvariant())
                {
                    case "USD": usd = perUnit; break;
                    case "EUR": eur = perUnit; break;
                    case "DEM": dem = perUnit; break;
                    case "XAU": xau = perUnit; break;
                }
            }

            if (usd is null && eur is null && dem is null && xau is null)
                return null;

            return new TcmbDayRates(date, usd, eur, dem, xau);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Xml.XmlException)
        {
            return null;
        }
    }

    private static bool TryParseTr(string? s, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(s) || s == ".")
            return false;
        return decimal.TryParse(
            s.Replace(',', '.'),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value);
    }

    private sealed record TcmbDayRates(
        DateTime Date,
        decimal? Usd,
        decimal? Eur,
        decimal? Dem,
        decimal? XauOnzTry);
}
