namespace SanalBorsa.Application.DTOs;

/// <summary>
/// Tek bir piyasa için o günün en çok kazandıran ve en çok kaybettiren enstrümanları.
/// </summary>
/// <param name="UniverseCount">
/// O tarihte bu piyasada fiyat verisi olan (dolayısıyla kazanan/kaybeden yarışına dahil olabilecek)
/// toplam enstrüman sayısı — "kaybettirenler" listesi küçük bir evrenden geliyorsa (ör. 2016'da
/// sadece 3 kripto varsa) kullanıcı bunu görüp yanılmasın diye.
/// </param>
public record TimeMachineDailyMarketReportDto(
    IReadOnlyList<TimeMachineLeaderDto> Gainers,
    IReadOnlyList<TimeMachineLeaderDto> Losers,
    int UniverseCount);

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
