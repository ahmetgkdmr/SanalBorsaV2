using MediatR;
using SanalBorsa.Application.DTOs;

namespace SanalBorsa.Application.Stocks.Queries.GetStockDetail;

public record GetStockDetailQuery(string Symbol) : IRequest<StockDetailDto>;
