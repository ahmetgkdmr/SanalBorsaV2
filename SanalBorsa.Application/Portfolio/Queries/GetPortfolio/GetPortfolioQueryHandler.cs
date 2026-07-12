using MediatR;
using SanalBorsa.Application.Common.Exceptions;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Portfolio.Queries.GetPortfolio;

public class GetPortfolioQueryHandler : IRequestHandler<GetPortfolioQuery, PortfolioDto>
{
    private readonly IUnitOfWork _uow;

    public GetPortfolioQueryHandler(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<PortfolioDto> Handle(GetPortfolioQuery request, CancellationToken cancellationToken)
    {
        var portfolio = await _uow.Portfolios.GetByUserIdAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("Portfolio", request.UserId);

        return PortfolioDto.FromEntity(portfolio);
    }
}
