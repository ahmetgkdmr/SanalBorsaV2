namespace SanalBorsa.Application.Common;

public static class SparklineHelper
{
    /// <summary>
    /// Intraday sparkline'ın başına önceki günün kapanışını ekler — badge'deki değişim yüzdesi
    /// bu referans noktasına göre hesaplandığı için, grafiğin de aynı noktadan başlaması gerekir
    /// (yoksa gap-up/gap-down günlerinde grafik yönü badge'in yönüyle çelişebilir).
    /// </summary>
    public static IReadOnlyList<decimal> PrependPreviousClose(
        IReadOnlyList<decimal> intradaySparkline,
        decimal? previousClose)
    {
        if (previousClose is not { } prev)
            return intradaySparkline;

        var withPrefix = new List<decimal>(intradaySparkline.Count + 1) { prev };
        withPrefix.AddRange(intradaySparkline);
        return withPrefix;
    }
}
