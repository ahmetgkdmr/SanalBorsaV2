namespace SanalBorsa.Domain.Models;

/// <summary>Toplu tarama için hafif kapanış satırı — entity materyalizasyonu yapılmaz.</summary>
public readonly record struct DailyClose(int StockId, DateTime Date, decimal Close);
