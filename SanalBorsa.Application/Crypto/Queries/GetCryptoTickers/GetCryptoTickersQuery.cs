using MediatR;
using SanalBorsa.Application.Common.Interfaces;

namespace SanalBorsa.Application.Crypto.Queries.GetCryptoTickers;

public record GetCryptoTickersQuery : IRequest<IReadOnlyList<CryptoTickerDto>>;

public class GetCryptoTickersQueryHandler
    : IRequestHandler<GetCryptoTickersQuery, IReadOnlyList<CryptoTickerDto>>
{
    private readonly ICryptoMarketService _crypto;

    public GetCryptoTickersQueryHandler(ICryptoMarketService crypto) => _crypto = crypto;

    public Task<IReadOnlyList<CryptoTickerDto>> Handle(
        GetCryptoTickersQuery request, CancellationToken cancellationToken) =>
        _crypto.GetTickersAsync(cancellationToken);
}
