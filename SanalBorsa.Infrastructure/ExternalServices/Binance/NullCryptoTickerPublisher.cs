using SanalBorsa.Application.Common.Interfaces;

namespace SanalBorsa.Infrastructure.ExternalServices.Binance;

public sealed class NullCryptoTickerPublisher : ICryptoTickerPublisher
{
    public Task PublishAsync(CryptoTickerDto ticker, CancellationToken ct = default) =>
        Task.CompletedTask;
}
