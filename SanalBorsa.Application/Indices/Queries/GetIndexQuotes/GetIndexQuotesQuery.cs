using MediatR;
using SanalBorsa.Application.DTOs;

namespace SanalBorsa.Application.Indices.Queries.GetIndexQuotes;

public record GetIndexQuotesQuery : IRequest<IReadOnlyList<IndexQuoteDto>>;
