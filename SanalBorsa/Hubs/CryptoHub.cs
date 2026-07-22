using Microsoft.AspNetCore.SignalR;
using SanalBorsa.Application.Common.Interfaces;

namespace SanalBorsa.API.Hubs;

public sealed class CryptoHub : Hub
{
    private const int SnapshotChunk = 40;

    private readonly ICryptoLiveTickerStore _store;

    public CryptoHub(ICryptoLiveTickerStore store) => _store = store;

    public override async Task OnConnectedAsync()
    {
        var snapshot = _store.GetTracked();
        for (var i = 0; i < snapshot.Count; i += SnapshotChunk)
        {
            var chunk = snapshot.Skip(i).Take(SnapshotChunk).ToList();
            if (chunk.Count > 0)
                await Clients.Caller.SendAsync("tickers", chunk);
        }

        await base.OnConnectedAsync();
    }
}
