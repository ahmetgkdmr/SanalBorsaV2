using MediatR;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Portfolio.Queries.GetPortfolioTransactions;

public record GetPortfolioTransactionsQuery(
    Guid UserId,
    int Page = 1,
    int PageSize = 10)
    : IRequest<PagedTransactionsDto>;

public record PagedTransactionsDto(
    IReadOnlyList<TransactionDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public class GetPortfolioTransactionsQueryHandler
    : IRequestHandler<GetPortfolioTransactionsQuery, PagedTransactionsDto>
{
    private readonly IUnitOfWork _uow;

    public GetPortfolioTransactionsQueryHandler(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<PagedTransactionsDto> Handle(
        GetPortfolioTransactionsQuery request,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var (items, total) = await _uow.Portfolios.GetTransactionsPagedAsync(
            request.UserId,
            page,
            pageSize,
            cancellationToken);

        var totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);

        var dtos = items.Select(t => new TransactionDto(
            t.Id,
            t.Symbol,
            PortfolioDto.MarketTypeToString(t.MarketType),
            t.Side == TxSide.Buy ? "buy" : "sell",
            t.Quantity,
            t.Price,
            t.Total,
            t.FillBreakdownJson,
            t.ExchangeRateAtTrade,
            t.ExecutedAt)).ToList();

        return new PagedTransactionsDto(dtos, page, pageSize, total, totalPages);
    }
}
