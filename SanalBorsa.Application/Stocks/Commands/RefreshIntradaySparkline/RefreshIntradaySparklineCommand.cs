using MediatR;
using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Application.Stocks.Commands.RefreshIntradaySparkline;

/// <summary>
/// Ana ekran sparkline'ı için önceki tam seans gününün 15dk bar'larını yeniler.
/// Market'e ait tablo tamamen silinip yeniden doldurulur — geçmişe dönük saklama yok.
/// </summary>
public record RefreshIntradaySparklineCommand(MarketType Market)
    : IRequest<RefreshIntradaySparklineResult>;

public record RefreshIntradaySparklineResult(
    int Attempted,
    int Synced,
    int Failed,
    int BarsWritten);
