using MediatR;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Application.Stocks.Queries.GetStockDetail;

public record GetStockDetailQuery(string Symbol, MarketType MarketType = MarketType.Bist)
    : IRequest<StockDetailDto>;
