using MediatR;
using SanalBorsa.Application.Common.Exceptions;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Portfolio.Queries.GetPublicTradeHistory;

public class GetPublicTradeHistoryQueryHandler
    : IRequestHandler<GetPublicTradeHistoryQuery, PublicTradeHistoryDto>
{
    private readonly IUnitOfWork _uow;

    public GetPublicTradeHistoryQueryHandler(IUnitOfWork uow) => _uow = uow;

    public async Task<PublicTradeHistoryDto> Handle(
        GetPublicTradeHistoryQuery request, CancellationToken ct)
    {
        var user = await _uow.Users.GetByUsernameAsync(request.Username, ct)
            ?? throw new NotFoundException("User", request.Username);

        // Gizlilik kapalıysa 404 DEĞİL, boş + işaretli yanıt dönüyoruz: kullanıcının var olduğu
        // zaten liderlik listesinden biliniyor, saklanan şey işlemleri. Böylece arayüz
        // "bu kullanıcı geçmişini paylaşmıyor" mesajını gösterebiliyor.
        if (!user.ShowTradeHistoryPublic)
            return new PublicTradeHistoryDto(user.Username, false, []);

        var take = Math.Clamp(request.Take, 1, 200);
        var (items, _) = await _uow.Portfolios.GetTransactionsPagedAsync(user.Id, 1, take, ct);

        var trades = items
            .Select(t => new PublicTradeDto(
                t.Symbol,
                PortfolioDto.MarketTypeToString(t.MarketType),
                t.Side.ToString().ToUpperInvariant(),
                t.Quantity,
                t.Price,
                t.ExecutedAt))
            .ToList();

        return new PublicTradeHistoryDto(user.Username, true, trades);
    }
}
