namespace SanalBorsa.Application.Common.Interfaces;

/// <summary>
/// Şüpheli (bir günde %20+ sıçrayan/düşen) bir fiyat bar'ı yazıldığında, belirli bir süre sonra
/// aynı sembol/tarihi TradingView'den tekrar çekip doğrulamak için kullanılır.
/// Uygulama: Infrastructure/Jobs/HangfirePriceAnomalyScheduler.cs (Hangfire delayed job).
/// </summary>
public interface IPriceAnomalyScheduler
{
    void ScheduleRecheck(string symbol, DateTime date, decimal previousClose, TimeSpan delay);
}
