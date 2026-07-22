using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using SanalBorsa.API.Hubs;
using SanalBorsa.Application.Common.Interfaces;

namespace SanalBorsa.API.Services;

/// <summary>
/// Binance tick'lerini batch + chunk ile SignalR'a basar.
/// Tek mesajda 468 coin (~100KB) göndermek istemci limitini aşıp canlı güncellemeyi öldürüyordu.
/// </summary>
public sealed class SignalRCryptoTickerPublisher : ICryptoTickerPublisher, IHostedService, IDisposable
{
    private const int ChunkSize = 40;
    private const int FlushMs = 200;

    private readonly IHubContext<CryptoHub> _hub;
    private readonly Channel<CryptoTickerDto> _channel =
        Channel.CreateUnbounded<CryptoTickerDto>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    private CancellationTokenSource? _cts;
    private Task? _loop;

    public SignalRCryptoTickerPublisher(IHubContext<CryptoHub> hub) => _hub = hub;

    public Task PublishAsync(CryptoTickerDto ticker, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(ticker, ct).AsTask();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = FlushLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();
        if (_cts is not null) await _cts.CancelAsync();
        if (_loop is not null)
        {
            try { await _loop; }
            catch (OperationCanceledException) { }
        }
    }

    private async Task FlushLoopAsync(CancellationToken ct)
    {
        var pending = new Dictionary<string, CryptoTickerDto>(StringComparer.OrdinalIgnoreCase);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(FlushMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            while (_channel.Reader.TryRead(out var t))
                pending[t.Symbol] = t;

            if (pending.Count == 0) continue;

            var batch = pending.Values.ToList();
            pending.Clear();

            try
            {
                for (var i = 0; i < batch.Count; i += ChunkSize)
                {
                    var chunk = batch.GetRange(i, Math.Min(ChunkSize, batch.Count - i));
                    await _hub.Clients.All.SendAsync("tickers", chunk, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // no clients / transient
            }
        }
    }

    public void Dispose() => _cts?.Dispose();
}
