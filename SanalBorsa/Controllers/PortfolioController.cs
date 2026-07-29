using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Application.Portfolio.Commands.BuyCrypto;
using SanalBorsa.Application.Portfolio.Commands.BuyStock;
using SanalBorsa.Application.Portfolio.Commands.SellCrypto;
using SanalBorsa.Application.Portfolio.Commands.SellStock;
using SanalBorsa.Application.Portfolio.Queries.GetPortfolio;
using SanalBorsa.Application.Portfolio.Queries.GetPortfolioTransactions;

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

    /// <summary>Kullanıcının portföyünü döner (nakit + holdings; işlem geçmişi yok).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PortfolioDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetPortfolioQuery(GetUserId()), ct);
        return Ok(result);
    }

    /// <summary>İşlem geçmişi — sayfalı.</summary>
    [HttpGet("transactions")]
    [ProducesResponseType(typeof(PagedTransactionsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTransactions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new GetPortfolioTransactionsQuery(GetUserId(), page, pageSize),
            ct);
        return Ok(result);
    }

    /// <summary>BIST hisse al (son kapanış fiyatıyla).</summary>
    [HttpPost("buy")]
    [ProducesResponseType(typeof(PortfolioDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Buy([FromBody] TradeRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new BuyStockCommand(GetUserId(), request.Symbol, request.Lots), ct);
        return Ok(result);
    }

    /// <summary>BIST hisse sat (son kapanış fiyatıyla).</summary>
    [HttpPost("sell")]
    [ProducesResponseType(typeof(PortfolioDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Sell([FromBody] TradeRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new SellStockCommand(GetUserId(), request.Symbol, request.Lots), ct);
        return Ok(result);
    }

    /// <summary>Kripto al — derinlik üzerinden erite-erite.</summary>
    [HttpPost("crypto/buy")]
    [ProducesResponseType(typeof(CryptoTradeResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> BuyCrypto([FromBody] CryptoTradeRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new BuyCryptoCommand(GetUserId(), request.Symbol, request.QuoteUsd, request.Quantity), ct);
        return Ok(result);
    }

    /// <summary>Kripto sat — derinlik üzerinden erite-erite.</summary>
    [HttpPost("crypto/sell")]
    [ProducesResponseType(typeof(CryptoTradeResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SellCrypto([FromBody] CryptoTradeRequest request, CancellationToken ct)
    {
        if (request.Quantity is null or <= 0)
            return BadRequest(new { message = "Satış için quantity gerekli." });

        var result = await _mediator.Send(
            new SellCryptoCommand(GetUserId(), request.Symbol, request.Quantity.Value), ct);
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

public record CryptoTradeRequest(string Symbol, decimal? QuoteUsd, decimal? Quantity);
