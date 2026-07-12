using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Application.Portfolio.Commands.BuyStock;
using SanalBorsa.Application.Portfolio.Commands.SellStock;
using SanalBorsa.Application.Portfolio.Queries.GetPortfolio;

namespace SanalBorsa.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
public class PortfolioController : ControllerBase
{
    private readonly IMediator _mediator;

    public PortfolioController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Kullanıcının portföyünü döner.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PortfolioDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetPortfolioQuery(GetUserId()), ct);
        return Ok(result);
    }

    /// <summary>Hisse al (son kapanış fiyatıyla).</summary>
    [HttpPost("buy")]
    [ProducesResponseType(typeof(PortfolioDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Buy([FromBody] TradeRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new BuyStockCommand(GetUserId(), request.Symbol, request.Lots), ct);
        return Ok(result);
    }

    /// <summary>Hisse sat (son kapanış fiyatıyla).</summary>
    [HttpPost("sell")]
    [ProducesResponseType(typeof(PortfolioDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Sell([FromBody] TradeRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new SellStockCommand(GetUserId(), request.Symbol, request.Lots), ct);
        return Ok(result);
    }

    private Guid GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue("sub")
               ?? throw new UnauthorizedAccessException("Token'da kullanıcı ID'si bulunamadı.");
        return Guid.Parse(sub);
    }
}

public record TradeRequest(string Symbol, long Lots);
