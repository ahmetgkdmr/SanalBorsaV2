using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;

namespace SanalBorsa.Infrastructure.ExternalServices.Binance;

/// <summary>
/// Binance <c>!miniTicker@arr</c> (küçük payload, ~1 sn).
/// Not: <c>!ticker@arr</c> çok büyük; receive takılıp store donuyordu.
/// </summary>
public sealed class BinanceTickerStreamService : BackgroundService
{
    private static readonly string[] WsBases =
    [
        "wss://data-stream.binance.vision",
        "wss://stream.binance.com:9443",
    ];

    private const string AllMiniTickerPath = "/ws/!miniTicker@arr";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICryptoLiveTickerStore _store;
    private readonly ICryptoTickerPublisher _publisher;
    private readonly ILogger<BinanceTickerStreamService> _logger;
    private readonly ConcurrentDictionary<string, decimal> _lastPublishedPrice =
        new(StringComparer.OrdinalIgnoreCase);

    private long _messages;
    private long _updates;

    public BinanceTickerStreamService(
        IServiceScopeFactory scopeFactory,
        ICryptoLiveTickerStore store,
        ICryptoTickerPublisher publisher,
        ILogger<BinanceTickerStreamService> logger)
    {
        _scopeFactory = scopeFactory;
        _store = store;
        _publisher = publisher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await BootstrapFromRestAsync(stoppingToken);

        var delay = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunLoopAsync(stoppingToken);
                delay = TimeSpan.FromSeconds(1);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Binance WS koptu, {Delay}s sonra yeniden denenecek", delay.TotalSeconds);
                try { await Task.Delay(delay, stoppingToken); }
                catch (OperationCanceledException) { break; }
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
        }
    }

    private async Task BootstrapFromRestAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var binance = scope.ServiceProvider.GetRequiredService<IBinanceMarketClient>();

            var filters = await binance.GetPriceFiltersAsync(ct);
            _store.SetAllowedSymbols(filters.Keys.ToList());
            _store.SetPriceDecimals(
                filters.ToDictionary(kv => kv.Key, kv => kv.Value.PriceDecimals, StringComparer.OrdinalIgnoreCase));
            _store.SetBaseAssets(
                filters.ToDictionary(kv => kv.Key, kv => kv.Value.BaseAsset, StringComparer.OrdinalIgnoreCase));

            var all = await binance.GetTickers24hrAsync(ct);
            var count = 0;
            foreach (var t in all.Where(x => filters.ContainsKey(x.Symbol)))
            {
                _store.Upsert(new CryptoTickerDto(
                    t.Symbol,
                    _store.GetBaseAsset(t.Symbol),
                    t.LastPrice,
                    t.PriceChangePercent,
                    t.QuoteVolume,
                    t.HighPrice,
                    t.LowPrice,
                    _store.GetPriceDecimals(t.Symbol)));
                count++;
            }

            _logger.LogInformation(
                "Crypto live store bootstrapped: {Count} USDT tickers, {Filters} allowed symbols",
                count,
                filters.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Crypto REST bootstrap başarısız — WS bekleniyor");
        }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        Exception? last = null;
        foreach (var baseUrl in WsBases)
        {
            try
            {
                await ConnectAndReadAsync(baseUrl + AllMiniTickerPath, ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                _logger.LogWarning(ex, "Binance WS bağlanamadı: {Base}", baseUrl);
            }
        }

        throw last ?? new InvalidOperationException("Binance WS bağlantısı kurulamadı.");
    }

    private async Task ConnectAndReadAsync(string url, CancellationToken ct)
    {
        using var ws = new ClientWebSocket();
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);

        _logger.LogInformation("Binance miniTicker WS bağlanıyor…");
        await ws.ConnectAsync(new Uri(url), ct);
        _logger.LogInformation("Binance miniTicker WS bağlandı (!miniTicker@arr)");

        var buffer = new byte[256 * 1024];
        var sb = new StringBuilder(64 * 1024);
        var lastHeartbeat = DateTime.UtcNow;

        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            sb.Clear();
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                    throw new InvalidOperationException("Binance WS close frame aldı.");
                }

                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            } while (!result.EndOfMessage);

            HandleMessage(sb.ToString());

            if ((DateTime.UtcNow - lastHeartbeat).TotalSeconds >= 30)
            {
                lastHeartbeat = DateTime.UtcNow;
                _logger.LogInformation(
                    "Crypto WS heartbeat: msgs={Messages}, updates={Updates}, sample BTC={Btc}",
                    Interlocked.Read(ref _messages),
                    Interlocked.Read(ref _updates),
                    _store.Get("BTCUSDT")?.Price);
            }
        }
    }

    private void HandleMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array) return;

            Interlocked.Increment(ref _messages);
            foreach (var item in root.EnumerateArray())
                ApplyMiniTicker(item);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WS mesaj parse hatası (len={Len})", json.Length);
        }
    }

    private void ApplyMiniTicker(JsonElement data)
    {
        if (!data.TryGetProperty("s", out var symEl)) return;
        var symbol = symEl.GetString()?.ToUpperInvariant();
        if (string.IsNullOrEmpty(symbol)) return;
        if (!_store.IsAllowed(symbol)) return;

        var price = ParseDec(data, "c");
        if (price <= 0) return;

        var open = ParseDec(data, "o");
        var changePct = open > 0 ? (price - open) / open * 100m : 0m;

        var dto = new CryptoTickerDto(
            symbol,
            _store.GetBaseAsset(symbol),
            price,
            changePct,
            ParseDec(data, "q"),
            ParseDec(data, "h"),
            ParseDec(data, "l"),
            _store.GetPriceDecimals(symbol));

        _store.Upsert(dto);
        Interlocked.Increment(ref _updates);

        if (_lastPublishedPrice.TryGetValue(symbol, out var prev) && prev == price)
            return;

        _lastPublishedPrice[symbol] = price;
        // Channel write — fire & forget (async void risk yok: TryWrite)
        _ = _publisher.PublishAsync(dto, CancellationToken.None);
    }

    private static decimal ParseDec(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p)) return 0m;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out var n)) return n;
        var s = p.GetString();
        return decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0m;
    }
}
