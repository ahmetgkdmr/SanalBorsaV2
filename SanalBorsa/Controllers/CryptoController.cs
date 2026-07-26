using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Application.Crypto.Commands.BackfillCryptoPreBinanceHistory;
using SanalBorsa.Application.Crypto.Commands.SyncCryptoHistory;
using SanalBorsa.Application.Crypto.Queries.GetCryptoDepth;
using SanalBorsa.Application.Crypto.Queries.GetCryptoStockMeta;
using SanalBorsa.Application.Crypto.Queries.GetCryptoTicker;
using SanalBorsa.Application.Crypto.Queries.GetCryptoTickers;
using SanalBorsa.Application.Crypto.Queries.GetCryptoTickerStrip;
using SanalBorsa.Application.Crypto.Queries.PreviewCryptoTrade;
using SanalBorsa.Application.DTOs;

namespace SanalBorsa.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class CryptoController : ControllerBase
{
    private readonly IMediator _mediator;

    public CryptoController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CryptoTickerDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetCryptoTickersQuery(), ct);
        return Ok(result);
    }

    /// <summary>
    /// Header kayan şeridinin sabit coin listesi (gün boyunca değişmez).
    /// </summary>
    [HttpGet("ticker-strip")]
    [ProducesResponseType(typeof(IReadOnlyList<CryptoStripItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> TickerStrip([FromQuery] int count, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetCryptoTickerStripQuery(count <= 0 ? 20 : count), ct);
        return Ok(result);
    }

    [HttpGet("{symbol}/meta")]
    [ProducesResponseType(typeof(StockDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Meta(string symbol, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetCryptoStockMetaQuery(symbol), ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("{symbol}")]
    [ProducesResponseType(typeof(CryptoTickerDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(string symbol, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetCryptoTickerQuery(symbol), ct);
        return Ok(result);
    }

    [HttpGet("{symbol}/depth")]
    [ProducesResponseType(typeof(CryptoDepthDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Depth(string symbol, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetCryptoDepthQuery(symbol), ct);
        return Ok(result);
    }

    [HttpPost("quote")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CryptoFillPreviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Quote([FromBody] CryptoQuoteRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new PreviewCryptoTradeQuery(request.Symbol, request.Side, request.QuoteUsd, request.Quantity),
            ct);
        return Ok(result);
    }

    /// <summary>
    /// Binance USDT geçmiş fiyat sync (seed + klines). Background çalışır.
    /// </summary>
    [HttpPost("sync-history")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public IActionResult SyncHistory(
        [FromServices] IServiceScopeFactory scopeFactory,
        [FromQuery] string? symbol = null,
        [FromQuery] bool full = false)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                await mediator.Send(new SyncCryptoHistoryCommand(symbol, full));
            }
            catch
            {
                // logged in handler
            }
        });

        return Accepted(new { message = "Crypto history sync started", symbol, full });
    }

    /// <summary>
    /// Tüm (veya tek) crypto için Binance listing öncesi USD geçmişini doldurur.
    /// Kaynak sırası: Zorinaq(BTC) → Coinbase → Yahoo. Binance günleri ezilmez.
    /// TradingView public API olmadığı için TV scrape yok; Yahoo/Coinbase TV'deki USD serilerine denk.
    /// </summary>
    [HttpPost("backfill-pre-binance")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public IActionResult BackfillPreBinance(
        [FromServices] IServiceScopeFactory scopeFactory,
        [FromQuery] string? symbol = null)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                await mediator.Send(new BackfillCryptoPreBinanceHistoryCommand(symbol));
            }
            catch
            {
                // logged in handler
            }
        });

        return Accepted(new
        {
            message = "Crypto pre-Binance USD backfill started for all active cryptos (or one symbol)",
            symbol,
            sources = new[] { "Zorinaq(BTC archive)", "Coinbase USD", "Yahoo Finance USD" },
            note = "Binance bars are never overwritten. TradingView has no public bulk API."
        });
    }
}

public record CryptoQuoteRequest(
    string Symbol,
    string Side,
    decimal? QuoteUsd,
    decimal? Quantity);
