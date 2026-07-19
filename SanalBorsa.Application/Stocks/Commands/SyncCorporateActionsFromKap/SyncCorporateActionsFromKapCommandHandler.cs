using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Exceptions;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Stocks.Commands.SyncCorporateActionsFromKap;

public class SyncCorporateActionsFromKapCommandHandler
    : IRequestHandler<SyncCorporateActionsFromKapCommand, SyncCorporateActionsFromKapResult>
{
    private readonly IUnitOfWork _uow;
    private readonly IKapCorporateActionService _kap;
    private readonly ILogger<SyncCorporateActionsFromKapCommandHandler> _logger;

    public SyncCorporateActionsFromKapCommandHandler(
        IUnitOfWork uow,
        IKapCorporateActionService kap,
        ILogger<SyncCorporateActionsFromKapCommandHandler> logger)
    {
        _uow = uow;
        _kap = kap;
        _logger = logger;
    }

    public async Task<SyncCorporateActionsFromKapResult> Handle(
        SyncCorporateActionsFromKapCommand request,
        CancellationToken cancellationToken)
    {
        var symbol = request.Symbol.Trim().ToUpperInvariant();
        var stock = await _uow.Stocks.GetBySymbolAsync(symbol, cancellationToken)
                    ?? throw new NotFoundException(nameof(Domain.Entities.Stock), symbol);

        _logger.LogInformation("KAP corporate-action sync started for {Symbol}", symbol);

        var incoming = await _kap.GetCorporateActionsAsync(symbol, sinceDate: null, cancellationToken);

        var removed = await _uow.CorporateActions.DeleteAllByStockIdAsync(stock.Id, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        foreach (var action in incoming)
        {
            action.StockId = stock.Id;
            await _uow.CorporateActions.AddAsync(action, cancellationToken);
        }

        await _uow.SaveChangesAsync(cancellationToken);

        var preview = incoming
            .Select(a =>
                $"{a.ActionDate:yyyy-MM-dd} {a.ActionType} value={a.Value}" +
                (a.SubscriptionPrice is null ? "" : $" ruchan={a.SubscriptionPrice}") +
                $" | {a.Description}")
            .ToList();

        _logger.LogInformation(
            "KAP corporate-action sync finished for {Symbol} — removed={Removed}, added={Added}",
            symbol, removed, incoming.Count);

        return new SyncCorporateActionsFromKapResult(symbol, removed, incoming.Count, preview);
    }
}
