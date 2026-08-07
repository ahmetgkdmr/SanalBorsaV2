namespace SanalBorsa.Application.Common;

/// <summary>
/// Bazı ABD hisseleri (ör. AAPL 1986, BRO 1981) USD/TRY paritesinden (kaynaklarımızda en erken
/// ~1989-11-07) daha eski fiyat geçmişine sahip. Zaman Makinesi'nde parite tabanından önceki bir
/// tarih seçilirse TL'ye çevrim için kur bulunamıyor ("Bu tarih için USD/TRY paritesi yok" hatası).
/// Ham fiyat geçmişi DB'de olduğu gibi kalır — sadece kullanıcıya sunulan "seçilebilir en erken
/// tarih" parite tabanının altına inmiyor, böylece hata hiç oluşmuyor.
/// </summary>
public static class EarliestDateClamp
{
    public static DateTime? Apply(DateTime? stockEarliest, DateTime? parityFloor)
    {
        if (stockEarliest is null) return null;
        if (parityFloor is null) return stockEarliest;
        return stockEarliest.Value < parityFloor.Value ? parityFloor.Value : stockEarliest.Value;
    }
}
