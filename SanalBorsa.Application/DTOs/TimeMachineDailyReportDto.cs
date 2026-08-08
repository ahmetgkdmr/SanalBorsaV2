namespace SanalBorsa.Application.DTOs;

/// <summary>
/// Tek bir piyasa için o günün en çok kazandıran ve en çok kaybettiren enstrümanları.
/// </summary>
public record TimeMachineDailyMarketReportDto(
    IReadOnlyList<TimeMachineLeaderDto> Gainers,
    IReadOnlyList<TimeMachineLeaderDto> Losers);

/// <summary>
/// "O gün ne alsaydım zengin olurdum?" — sadece tarih girilerek BIST/Kripto/ABD için
/// önceden hesaplanmış en çok kazandıran 3 ve en çok kaybettiren 3 enstrüman.
/// </summary>
public record TimeMachineDailyReportDto(
    string RequestedDate,
    TimeMachineDailyMarketReportDto Bist,
    TimeMachineDailyMarketReportDto Crypto,
    TimeMachineDailyMarketReportDto UsStocks,
    DateTime? ComputedAt);
