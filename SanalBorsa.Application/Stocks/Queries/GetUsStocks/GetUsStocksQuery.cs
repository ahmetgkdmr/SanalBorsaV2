using MediatR;
using SanalBorsa.Application.Common.Models;
using SanalBorsa.Application.DTOs;

namespace SanalBorsa.Application.Stocks.Queries.GetUsStocks;

/// <summary>
/// Pilot listesi (10 sembol) — BIST'in sayfalama/index-filtre/top-gainers karmaşası yok,
/// sadece Yahoo'dan senkronlanmış son fiyat + sparkline.
/// </summary>
public record GetUsStocksQuery(bool? IsActive = true) : IRequest<PagedResult<StockDto>>;
