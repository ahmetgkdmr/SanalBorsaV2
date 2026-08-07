using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;

namespace SanalBorsa.Infrastructure.ExternalServices.TradingView;

/// <summary>
/// TradingView WS'in gerçek zamanlı "quote" protokolü (chart/bar çekmekten AYRI bir mekanizma —
/// quote_create_session + quote_add_symbols) ile USD/TRY, EUR/TRY ve gram altın/TRY'yi saniyeler
/// içinde güncellenen gerçek forex/emtia kurundan çeker. Binance'in USDT/TRY paritesinden farklı
/// olarak, bu gerçek interbank/forex kaynağıdır (kullanıcı ile karşılaştırmalı doğrulandı).
///
/// Aynı canlı ticker store'u ve SignalR yayın hattını (ICryptoLiveTickerStore/ICryptoTickerPublisher)
/// kripto akışıyla PAYLAŞIR — frontend'in zaten dinlediği "tickers" event'inde bu üç sembol de
/// (USDTRY, EURTRY, GRAMALTIN) otomatik gelir, yeni bir SignalR bağlantısı gerekmez.
/// </summary>
public sealed class TvFxTickerStreamService : BackgroundService
{
    private const string WsUrl = "wss://data.tradingview.com/socket.io/websocket";
    private const string AuthToken = "unauthorized_user_token";
    private const decimal GramsPerTroyOunce = 31.1034768m;

    private static readonly (string TvSymbol, string StoreSymbol)[] Symbols =
    [
        ("FX_IDC:USDTRY", "USDTRY"),
        ("FX_IDC:EURTRY", "EURTRY"),
        ("FX_IDC:XAUTRY", "GRAMALTIN"), // ons → gram'a bölünerek saklanır
    ];

    private readonly ICryptoLiveTickerStore _store;
    private readonly ICryptoTickerPublisher _publisher;
    private readonly ILogger<TvFxTickerStreamService> _logger;

    private readonly Dictionary<string, decimal> _lastPrice = new(StringComparer.OrdinalIgnoreCase);

    public TvFxTickerStreamService(
        ICryptoLiveTickerStore store,
        ICryptoTickerPublisher publisher,
        ILogger<TvFxTickerStreamService> logger)
    {
        _store = store;
        _publisher = publisher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(2);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
                delay = TimeSpan.FromSeconds(2);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TV FX quote WS koptu, {Delay}s sonra yeniden denenecek", delay.TotalSeconds);
                try { await Task.Delay(delay, stoppingToken); }
                catch (OperationCanceledException) { break; }
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Origin", "https://data.tradingview.com");
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(TimeSpan.FromSeconds(15));
        await ws.ConnectAsync(new Uri(WsUrl), connectCts.Token);
        _logger.LogInformation("TV FX quote WS bağlandı");

        _store.SetPriceDecimals(Symbols.ToDictionary(s => s.StoreSymbol, s => s.StoreSymbol == "GRAMALTIN" ? 2 : 4));

        var quoteSession = "qs_" + Guid.NewGuid().ToString("N")[..12];
        await SendAsync(ws, "set_auth_token", [AuthToken], ct);
        await SendAsync(ws, "quote_create_session", [quoteSession], ct);
        await SendAsync(ws, "quote_set_fields", [quoteSession, "lp", "chp"], ct);
        foreach (var (tvSymbol, _) in Symbols)
            await SendAsync(ws, "quote_add_symbols", [quoteSession, tvSymbol], ct);

        var buffer = new byte[64 * 1024];
        var pending = new StringBuilder();

        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var result = await ws.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new InvalidOperationException("TV FX quote WS close frame aldı.");

            pending.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (!result.EndOfMessage)
                continue;

            var leftover = DrainFrames(pending.ToString(), out var payloads);
            pending.Clear();
            if (leftover.Length > 0)
                pending.Append(leftover);

            foreach (var payload in payloads)
            {
                if (payload.StartsWith("~h~", StringComparison.Ordinal))
                {
                    var framed = $"~m~{payload.Length}~m~{payload}";
                    await ws.SendAsync(Encoding.UTF8.GetBytes(framed), WebSocketMessageType.Text, true, ct);
                    continue;
                }

                HandlePayload(payload);
            }
        }
    }

    private void HandlePayload(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (!root.TryGetProperty("m", out var mEl) || mEl.GetString() != "qsd")
                return;
            if (!root.TryGetProperty("p", out var pEl) || pEl.GetArrayLength() < 2)
                return;

            var data = pEl[1];
            if (!data.TryGetProperty("n", out var nEl))
                return;
            var tvSymbol = nEl.GetString();
            var match = Array.Find(Symbols, s => s.TvSymbol == tvSymbol);
            if (match.TvSymbol is null)
                return;
            if (!data.TryGetProperty("v", out var vEl))
                return;

            var lastKnown = _lastPrice.TryGetValue(match.StoreSymbol, out var lp) ? lp : 0m;

            decimal? price = vEl.TryGetProperty("lp", out var lpEl) && lpEl.TryGetDecimal(out var lpv)
                ? lpv
                : null;
            decimal? changePct = vEl.TryGetProperty("chp", out var chpEl) && chpEl.TryGetDecimal(out var chpv)
                ? chpv
                : null;

            if (price is null && changePct is null)
                return; // sadece bid/ask geldi, gösterge için ihtiyacımız olan lp/chp değil

            var effectivePrice = price ?? lastKnown;
            if (effectivePrice <= 0)
                return;

            var storePrice = match.StoreSymbol == "GRAMALTIN" ? effectivePrice / GramsPerTroyOunce : effectivePrice;
            _lastPrice[match.StoreSymbol] = effectivePrice;

            var dto = new CryptoTickerDto(
                match.StoreSymbol,
                match.StoreSymbol,
                storePrice,
                changePct ?? 0m,
                0m,
                storePrice,
                storePrice,
                match.StoreSymbol == "GRAMALTIN" ? 2 : 4);

            _store.UpsertAlways(dto);
            _ = _publisher.PublishAsync(dto, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "TV FX quote payload parse hatası: {Payload}", payload);
        }
    }

    private static async Task SendAsync(ClientWebSocket ws, string func, object[] args, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object?> { ["m"] = func, ["p"] = args });
        var framed = $"~m~{json.Length}~m~{json}";
        await ws.SendAsync(Encoding.UTF8.GetBytes(framed), WebSocketMessageType.Text, true, ct);
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
                if (next < 0) return text[i..];
                i = next;
                continue;
            }

            var lenStart = i + 3;
            var lenEnd = text.IndexOf("~m~", lenStart, StringComparison.Ordinal);
            if (lenEnd < 0) return text[i..];

            if (!int.TryParse(text.AsSpan(lenStart, lenEnd - lenStart), out var len) || len < 0)
                return text[i..];

            var payloadStart = lenEnd + 3;
            if (payloadStart + len > text.Length) return text[i..];

            payloads.Add(text.Substring(payloadStart, len));
            i = payloadStart + len;
        }

        return "";
    }
}
