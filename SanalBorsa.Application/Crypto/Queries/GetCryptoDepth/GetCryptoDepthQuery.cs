using MediatR;
using SanalBorsa.Application.Common.Interfaces;

namespace SanalBorsa.Application.Crypto.Queries.GetCryptoDepth;

public record GetCryptoDepthQuery(string Symbol) : IRequest<CryptoDepthDto>;

public class GetCryptoDepthQueryHandler : IRequestHandler<GetCryptoDepthQuery, CryptoDepthDto>
{
    private readonly ICryptoMarketService _crypto;

    public GetCryptoDepthQueryHandler(ICryptoMarketService crypto) => _crypto = crypto;

    public Task<CryptoDepthDto> Handle(GetCryptoDepthQuery request, CancellationToken cancellationToken) =>
        _crypto.GetDepthAsync(request.Symbol, cancellationToken);
}
