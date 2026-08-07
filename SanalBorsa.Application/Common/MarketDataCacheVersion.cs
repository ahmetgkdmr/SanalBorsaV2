namespace SanalBorsa.Application.Common;

/// <summary>
/// BIST/ABD piyasa listesi cache anahtarına gömülen sürüm sayacı. Gece senkron job'ları
/// (fiyat/kurumsal olay/sparkline/dönem şampiyonları) kendi piyasasını her güncellediğinde
/// bump eder — eski cache girdileri artık hiçbir anahtarla eşleşmediği için görünmez olur,
/// bir sonraki istek yeni sürüm altında taze veriyi hesaplayıp cache'e yazar.
/// </summary>
public sealed class MarketDataCacheVersion
{
    private int _bist;
    private int _us;

    public int Bist => _bist;
    public int Us => _us;

    public void BumpBist() => Interlocked.Increment(ref _bist);
    public void BumpUs() => Interlocked.Increment(ref _us);
}
