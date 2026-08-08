using MediatR;
using SanalBorsa.Application.DTOs;

namespace SanalBorsa.Application.Stocks.Queries.GetTimeMachineDailyReport;

/// <summary>
/// "O gün ne alsaydım zengin olurdum?" — verilen tarihten bugüne BIST/Kripto/ABD için
/// en çok kazandıran 3 ve en çok kaybettiren 3 enstrüman. Tarih işlem günü değilse
/// önceki en yakın işlem gününe kayılır.
/// </summary>
public record GetTimeMachineDailyReportQuery(DateTime Date) : IRequest<TimeMachineDailyReportDto>;
