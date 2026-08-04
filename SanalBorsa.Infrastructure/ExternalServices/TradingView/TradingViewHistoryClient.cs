using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Infrastructure.ExternalServices.TradingView;

/// <summary>
/// TradingView WebSocket history (tvDatafeed protokolü).
/// <c>adjustment=none</c> → ham OHLCV; <c>dividends</c> → AdjustedClose.
/// </summary>
public sealed class TradingViewHistoryClient : ITradingViewHistoryService, IAsyncDisposable
{
    private const string WsUrl = "wss://data.tradingview.com/socket.io/websocket";
    private const string AuthToken = "unauthorized_user_token";
    private const int MaxBars = 10_000;
    private const int MoreBarsChunk = 5_000;
    private const int MaxMoreRequests = 80;
    private const int ReceiveBufferSize = 1 << 16;

    private static readonly Regex HeartbeatRx = new(
        @"^~h~\d+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BarRx = new(
        @"\{""i"":\d+,""v"":\[([0-9.eE+\-]+),([0-9.eE+\-]+),([0-9.eE+\-]+),([0-9.eE+\-]+),([0-9.eE+\-]+),([0-9.eE+\-]+)\]\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ILogger<TradingViewHistoryClient> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private ClientWebSocket? _ws;
    private string _chartSession = "";
    private int _symbolSeq;
    private readonly ConcurrentQueue<string> _inbox = new();
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveLoop;

    public TradingViewHistoryClient(ILogger<TradingViewHistoryClient> logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<StockPriceHistory>> GetBistDailyBarsAsync(
        string bistSymbol,
        DateTime from,
        DateTime to,
        CancellationToken ct = default)
        => GetDailyBarsByTvSymbolAsync($"BIST:{bistSymbol.Trim().ToUpperInvariant()}", from, to, ct);

    public async Task<IReadOnlyDictionary<DateTime, decimal>> GetBistAdjustedClosesAsync(
        string bistSymbol,
        DateTime from,
        DateTime to,
        CancellationToken ct = default)
    {
        var bars = await FetchBarsAsync(
            $"BIST:{bistSymbol.Trim().ToUpperInvariant()}", from, to, adjustment: "dividends", ct);
        return bars.ToDictionary(b => b.Date.Date, b => b.Close);
    }

    /// <summary>Geriye uyumluluk — BIST ham.</summary>
    public Task<IReadOnlyList<StockPriceHistory>> GetDailyBarsAsync(
        string bistSymbol,
        DateTime from,
        DateTime to,
        CancellationToken ct = default)
        => GetBistDailyBarsAsync(bistSymbol, from, to, ct);

    public async Task<IReadOnlyDictionary<DateTime, decimal>> GetAdjustedClosesAsync(
        string bistSymbol,
        DateTime from,
        DateTime to,
        CancellationToken ct = default)
        => await GetBistAdjustedClosesAsync(bistSymbol, from, to, ct);

    public Task<IReadOnlyList<StockPriceHistory>> GetDailyBarsByTvSymbolAsync(
        string tvSymbol,
        DateTime from,
        DateTime to,
        CancellationToken ct = default)
        => FetchBarsAsync(tvSymbol.Trim(), from, to, adjustment: "none", ct);

    public async Task<IReadOnlyDictionary<DateTime, decimal>> GetAdjustedClosesByTvSymbolAsync(
        string tvSymbol,
        DateTime from,
        DateTime to,
        CancellationToken ct = default)
    {
        var bars = await FetchBarsAsync(tvSymbol.Trim(), from, to, adjustment: "dividends", ct);
        return bars.ToDictionary(b => b.Date.Date, b => b.Close);
    }

    private async Task<IReadOnlyList<StockPriceHistory>> FetchBarsAsync(
        string symbol,
        DateTime from,
        DateTime to,
        string adjustment,
        CancellationToken ct)
    {
        var fromDate = from.Date;
        var toDate = to.Date;
        if (fromDate > toDate)
            return [];

        var calendarDays = (int)(toDate - fromDate).TotalDays + 1;
        var nBars = fromDate <= new DateTime(1971, 1, 1)
            ? MaxBars
            : Math.Clamp((int)(calendarDays * 1.5) + 20, 20, MaxBars);

        await _gate.WaitAsync(ct);
        try
        {
            await EnsureConnectedAsync(ct);

            var raw = await RequestSeriesWithHistoryAsync(symbol, adjustment, nBars, fromDate, ct);
            if (string.IsNullOrEmpty(raw) ||
                raw.Contains("symbol_error", StringComparison.Ordinal) ||
                raw.Contains("series_error", StringComparison.Ordinal))
            {
                _logger.LogDebug(
                    "TV WS: {Symbol} adj={Adj} ilk deneme boş/hata — reconnect",
                    symbol, adjustment);
                await TryReconnectAsync(CancellationToken.None);
                await EnsureConnectedAsync(ct);
                raw = await RequestSeriesWithHistoryAsync(symbol, adjustment, nBars, fromDate, ct);
            }

            if (string.IsNullOrEmpty(raw))
            {
                _logger.LogWarning("TV WS: {Symbol} adj={Adj} timeout/empty", symbol, adjustment);
                return [];
            }

            if (raw.Contains("symbol_error", StringComparison.Ordinal) ||
                raw.Contains("series_error", StringComparison.Ordinal))
            {
                _logger.LogDebug("TV WS: {Symbol} adj={Adj} resolve/series error", symbol, adjustment);
                return [];
            }

            var bars = ParseBars(raw, fromDate, toDate, adjustment);
            if (bars.Count > 0)
            {
                _logger.LogInformation(
                    "TV WS: {Symbol} adj={Adj} {Count} bars ({From:yyyy-MM-dd} → {To:yyyy-MM-dd})",
                    symbol, adjustment, bars.Count, bars[0].Date, bars[^1].Date);
            }

            return bars;
        }
        catch (Exception ex) when (ex is WebSocketException or IOException)
        {
            _logger.LogWarning(ex, "TV WS failed for {Symbol} adj={Adj}", symbol, adjustment);
            await TryReconnectAsync(CancellationToken.None);
            return [];
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// İlk seriyi çeker; <paramref name="fromDate"/> kapsanana kadar <c>request_more_data</c> ile geriye kayar.
    /// </summary>
    private async Task<string?> RequestSeriesWithHistoryAsync(
        string symbol,
        string adjustment,
        int nBars,
        DateTime fromDate,
        CancellationToken ct)
    {
        var seq = Interlocked.Increment(ref _symbolSeq);
        _chartSession = "cs_" + Guid.NewGuid().ToString("N")[..12];
        var symId = $"sds_sym_{seq}";
        var seriesId = $"sds_{seq}";
        var seriesName = $"s{seq}";

        while (_inbox.TryDequeue(out _)) { }

        await SendAsync("chart_create_session", [_chartSession, ""], ct);

        var resolveArg =
            "={\"symbol\":\"" + symbol + "\",\"adjustment\":\"" + adjustment + "\",\"session\":\"regular\"}";

        await SendAsync("resolve_symbol", [_chartSession, symId, resolveArg], ct);
        await SendAsync("create_series",
            [_chartSession, seriesId, seriesName, symId, "1D", nBars, ""], ct);

        var acc = new StringBuilder();
        var first = await WaitForSeriesAsync(TimeSpan.FromSeconds(25), ct);
        if (string.IsNullOrEmpty(first))
            return null;

        acc.Append(first);
        if (first.Contains("symbol_error", StringComparison.Ordinal) ||
            first.Contains("series_error", StringComparison.Ordinal))
            return acc.ToString();

        for (var i = 0; i < MaxMoreRequests; i++)
        {
            var earliest = EarliestBarDate(acc.ToString());
            if (earliest is null || earliest.Value <= fromDate)
                break;

            var beforeCount = BarRx.Matches(acc.ToString()).Count;
            await SendAsync("request_more_data", [_chartSession, seriesId, MoreBarsChunk], ct);
            var more = await WaitForSeriesAsync(TimeSpan.FromSeconds(20), ct);
            if (string.IsNullOrEmpty(more))
                break;

            acc.Append(more);
            var afterCount = BarRx.Matches(acc.ToString()).Count;
            if (afterCount <= beforeCount)
                break;
        }

        return acc.ToString();
    }

    private static DateTime? EarliestBarDate(string raw)
    {
        DateTime? min = null;
        foreach (Match m in BarRx.Matches(raw))
        {
            if (!TryParseInv(m.Groups[1].Value, out var ts))
                continue;
            var date = DateTimeOffset.FromUnixTimeSeconds((long)ts).UtcDateTime.Date;
            if (min is null || date < min)
                min = date;
        }

        return min;
    }

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_ws is { State: WebSocketState.Open } && _receiveLoop is { IsCompleted: false })
            return;

        await DisconnectCoreAsync();

        var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Origin", "https://data.tradingview.com");
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(TimeSpan.FromSeconds(15));
        await ws.ConnectAsync(new Uri(WsUrl), connectCts.Token);

        _ws = ws;
        _chartSession = "cs_" + Guid.NewGuid().ToString("N")[..12];
        _symbolSeq = 0;

        _receiveCts = new CancellationTokenSource();
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token));

        await SendAsync("set_auth_token", [AuthToken], ct);
        _logger.LogInformation("TradingView WebSocket bağlandı");
    }

    private async Task TryReconnectAsync(CancellationToken ct)
    {
        try
        {
            await DisconnectCoreAsync();
            if (!ct.IsCancellationRequested)
                await EnsureConnectedAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "TV WS reconnect failed");
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buf = new byte[ReceiveBufferSize];
        var pending = new StringBuilder();

        try
        {
            while (!ct.IsCancellationRequested && _ws is { State: WebSocketState.Open })
            {
                var result = await _ws.ReceiveAsync(buf, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                pending.Append(Encoding.UTF8.GetString(buf, 0, result.Count));
                if (!result.EndOfMessage)
                    continue;

                var leftover = DrainFrames(pending.ToString(), out var payloads);
                pending.Clear();
                if (leftover.Length > 0)
                    pending.Append(leftover);

                foreach (var payload in payloads)
                {
                    if (HeartbeatRx.IsMatch(payload))
                    {
                        try
                        {
                            var framed = $"~m~{payload.Length}~m~{payload}";
                            var bytes = Encoding.UTF8.GetBytes(framed);
                            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
                        }
                        catch
                        {
                            // outer reconnect
                        }

                        continue;
                    }

                    _inbox.Enqueue(payload);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "TV WS receive loop ended");
        }
    }

    private static string DrainFrames(string text, out List<string> payloads)
    {
        payloads = [];
        var i = 0;
        while (i < text.Length)
        {
            if (i + 3 > text.Length || text[i] != '~' || text[i + 1] != 'm' || text[i + 2] != '~')
            {
                var next = text.IndexOf("~m~", i + 1, StringComparison.Ordinal);
                if (next < 0)
                    return text[i..];
                i = next;
                continue;
            }

            var lenStart = i + 3;
            var lenEnd = text.IndexOf("~m~", lenStart, StringComparison.Ordinal);
            if (lenEnd < 0)
                return text[i..];

            if (!int.TryParse(text.AsSpan(lenStart, lenEnd - lenStart), out var len) || len < 0)
                return text[i..];

            var payloadStart = lenEnd + 3;
            if (payloadStart + len > text.Length)
                return text[i..];

            payloads.Add(text.Substring(payloadStart, len));
            i = payloadStart + len;
        }

        return "";
    }

    private async Task<string?> WaitForSeriesAsync(TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        var acc = new StringBuilder();
        DateTime? barsSeenAt = null;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            var drained = false;
            while (_inbox.TryDequeue(out var payload))
            {
                drained = true;
                acc.Append(payload);

                if (payload.Contains("symbol_error", StringComparison.Ordinal) ||
                    payload.Contains("series_error", StringComparison.Ordinal))
                {
                    return acc.ToString();
                }

                if (payload.Contains("series_completed", StringComparison.Ordinal))
                    return acc.ToString();

                if (BarRx.IsMatch(payload))
                    barsSeenAt ??= DateTime.UtcNow;
            }

            if (barsSeenAt is not null &&
                DateTime.UtcNow - barsSeenAt.Value >= TimeSpan.FromMilliseconds(1200))
            {
                return acc.ToString();
            }

            if (!drained)
                await Task.Delay(40, ct);
        }

        var leftover = acc.ToString();
        return leftover.Length > 0 && BarRx.IsMatch(leftover) ? leftover : null;
    }

    private async Task SendAsync(string func, object[] args, CancellationToken ct)
    {
        if (_ws is not { State: WebSocketState.Open })
            throw new WebSocketException("TradingView WebSocket açık değil");

        var json = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["m"] = func,
            ["p"] = args,
        });
        var framed = $"~m~{json.Length}~m~{json}";
        var bytes = Encoding.UTF8.GetBytes(framed);
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    private static List<StockPriceHistory> ParseBars(string raw, DateTime fromDate, DateTime toDate, string adjustment)
    {
        var now = DateTime.UtcNow;
        var list = new List<StockPriceHistory>();
        var seen = new HashSet<DateTime>();
        // "dividends" (düzenlenmiş) seride kümülatif getiri çok eski/çok bölünmüş hisselerde
        // kuruşun binde birine inebiliyor (ör. GARAN 1991: ₺0,00012) — 4 basamağa yuvarlama bunu
        // sıfıra yakın bir sabite ezip Zaman Makinesi hesabını %20+ saptırıyordu. Ham ("none") seri
        // gerçek işlem fiyatı olduğu için 4 basamak zaten yeterli, DB kolonuyla (decimal 18,4) da
        // tutarlı — sadece düzenlenmiş taraf için hassasiyeti artırıyoruz.
        var decimals = adjustment == "dividends" ? 10 : 4;

        foreach (Match m in BarRx.Matches(raw))
        {
            if (!TryParseInv(m.Groups[1].Value, out var ts) ||
                !TryParseInv(m.Groups[2].Value, out var open) ||
                !TryParseInv(m.Groups[3].Value, out var high) ||
                !TryParseInv(m.Groups[4].Value, out var low) ||
                !TryParseInv(m.Groups[5].Value, out var close) ||
                !TryParseInv(m.Groups[6].Value, out var volume))
            {
                continue;
            }

            if (close <= 0)
                continue;

            var date = DateTimeOffset.FromUnixTimeSeconds((long)ts).UtcDateTime.Date;
            if (date < fromDate || date > toDate)
                continue;

            if (!seen.Add(date))
            {
                // Aynı güne birden fazla bar gelirse sonuncuyu tut (forming → finalized).
                var idx = list.FindIndex(b => b.Date == date);
                if (idx >= 0) list.RemoveAt(idx);
                else continue;
            }

            var c = Math.Round((decimal)close, decimals);
            list.Add(new StockPriceHistory
            {
                Date = date,
                Open = Math.Round((decimal)open, decimals),
                High = Math.Round((decimal)high, decimals),
                Low = Math.Round((decimal)low, decimals),
                Close = c,
                AdjustedClose = c,
                Volume = (long)Math.Max(0, Math.Round(volume)),
                CreatedAt = now,
            });
        }

        list.Sort((a, b) => a.Date.CompareTo(b.Date));
        return list;
    }

    private static bool TryParseInv(string s, out double value) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private async Task DisconnectCoreAsync()
    {
        try { _receiveCts?.Cancel(); } catch { /* ignore */ }

        if (_receiveLoop is not null)
        {
            try { await Task.WhenAny(_receiveLoop, Task.Delay(1500)); } catch { /* ignore */ }
        }

        if (_ws is not null)
        {
            try
            {
                if (_ws.State == WebSocketState.Open)
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            }
            catch { /* ignore */ }

            _ws.Dispose();
            _ws = null;
        }

        _receiveCts?.Dispose();
        _receiveCts = null;
        _receiveLoop = null;
        while (_inbox.TryDequeue(out _)) { }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectCoreAsync();
        _gate.Dispose();
    }
}
