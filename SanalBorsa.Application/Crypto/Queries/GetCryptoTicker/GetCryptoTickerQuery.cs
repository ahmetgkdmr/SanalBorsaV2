using MediatR;
using SanalBorsa.Application.Common.Exceptions;
using SanalBorsa.Application.Common.Interfaces;

namespace SanalBorsa.Application.Crypto.Queries.GetCryptoTicker;

public record GetCryptoTickerQuery(string Symbol) : IRequest<CryptoTickerDto>;

public class GetCryptoTickerQueryHandler : IRequestHandler<GetCryptoTickerQuery, CryptoTickerDto>
{
    private readonly ICryptoMarketService _crypto;

    public GetCryptoTickerQueryHandler(ICryptoMarketService crypto) => _crypto = crypto;

    public async Task<CryptoTickerDto> Handle(GetCryptoTickerQuery request, CancellationToken cancellationToken)
    {
        return await _crypto.GetTickerAsync(request.Symbol, cancellationToken)
            ?? throw new NotFoundException("Crypto", request.Symbol);
    }
}
