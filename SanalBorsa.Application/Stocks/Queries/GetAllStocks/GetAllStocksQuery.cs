using MediatR;
using SanalBorsa.Application.Common.Models;
using SanalBorsa.Application.DTOs;

namespace SanalBorsa.Application.Stocks.Queries.GetAllStocks;

public record GetAllStocksQuery(
    int Page = 1,
    int PageSize = 50,
    string? Search = null,
    bool? IsActive = true
) : IRequest<PagedResult<StockDto>>;
