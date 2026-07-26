namespace SanalBorsa.Domain.Enums;

/// <summary>Zaman makinesi "o gün alsaydın bugün" liderlik tablosunun kategorileri.</summary>
public enum TimeMachineCategory
{
    /// <summary>Borsa İstanbul hisseleri — günlük en çok kazandıran 5 hisse.</summary>
    Bist = 1,

    /// <summary>Kripto (Binance USDT çiftleri) — günlük en çok kazandıran 5 coin.</summary>
    Crypto = 2,

    /// <summary>USD/TRY, EUR/TRY, gram altın — her gün sabit 3 satır.</summary>
    Parity = 3,
}
