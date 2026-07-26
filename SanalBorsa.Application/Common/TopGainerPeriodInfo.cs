using SanalBorsa.Domain.Enums;

namespace SanalBorsa.Application.Common;

public static class TopGainerPeriodInfo
{
    public static string Key(TopGainerPeriod p) => p switch
    {
        TopGainerPeriod.Week => "week",
        TopGainerPeriod.Month => "month",
        TopGainerPeriod.Year => "year",
        TopGainerPeriod.FiveYear => "fiveyear",
        TopGainerPeriod.TenYear => "tenyear",
        _ => p.ToString().ToLowerInvariant(),
    };

    public static string Label(TopGainerPeriod p) => p switch
    {
        TopGainerPeriod.Week => "Son 1 haftanın en çok kazananı",
        TopGainerPeriod.Month => "Son 1 ayın en çok kazananı",
        TopGainerPeriod.Year => "Son 1 yılın en çok kazananı",
        TopGainerPeriod.FiveYear => "Son 5 yılın en çok kazananı",
        TopGainerPeriod.TenYear => "Son 10 yılın en çok kazananı",
        _ => "En çok kazanan",
    };

    /// <summary>Kart ribbon / dar alan için kısa başlık.</summary>
    public static string ShortLabel(TopGainerPeriod p) => p switch
    {
        TopGainerPeriod.Week => "Son 1 hafta",
        TopGainerPeriod.Month => "Son 1 ay",
        TopGainerPeriod.Year => "Son 1 yıl",
        TopGainerPeriod.FiveYear => "Son 5 yıl",
        TopGainerPeriod.TenYear => "Son 10 yıl",
        _ => "Şampiyon",
    };

    public static int SortOrder(TopGainerPeriod p) => p switch
    {
        TopGainerPeriod.Week => 0,
        TopGainerPeriod.Month => 1,
        TopGainerPeriod.Year => 2,
        TopGainerPeriod.FiveYear => 3,
        TopGainerPeriod.TenYear => 4,
        _ => 9,
    };
}
