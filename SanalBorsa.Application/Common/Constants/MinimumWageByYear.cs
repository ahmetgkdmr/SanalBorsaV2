namespace SanalBorsa.Application.Common.Constants;

/// <summary>
/// Net asgari ücret (16+). Fiyat serisi Yeni TL cinsinden olduğu için
/// 2005 öncesi resmi (eski) TL tutarları 1.000.000'a bölünerek saklanır.
/// </summary>
public static class MinimumWageByYear
{
    /// <summary>Dönem başlangıcı (dahil) → net aylık ücret (Yeni TL).</summary>
    private static readonly (DateTime From, decimal NetYtl)[] Periods =
    [
        (new(1990, 1, 1), 0.146475m),
        (new(1990, 8, 1), 0.261954m),
        (new(1991, 1, 1), 0.266454m),
        (new(1991, 8, 1), 0.502911m),
        (new(1992, 1, 1), 0.511911m),
        (new(1992, 8, 1), 0.907839m),
        (new(1993, 1, 1), 0.922839m),
        (new(1993, 8, 1), 1.563473m),
        (new(1994, 1, 1), 1.713435m),
        (new(1994, 9, 1), 2.759429m),
        (new(1995, 1, 1), 2.834429m),
        (new(1995, 9, 1), 5.514192m),
        (new(1996, 1, 1), 5.739193m),
        (new(1996, 8, 1), 11.339802m),
        (new(1997, 1, 1), 11.422152m),
        (new(1997, 8, 1), 23.474588m),
        (new(1998, 1, 1), 24.518025m),
        (new(1998, 8, 1), 33.808514m),
        (new(1999, 1, 1), 58.948065m),
        (new(1999, 7, 1), 70.222320m),
        (new(2000, 1, 1), 82.417500m),
        (new(2000, 7, 1), 86.922900m),
        (new(2001, 1, 1), 102.369600m),
        (new(2001, 8, 1), 122.186520m),
        (new(2002, 1, 1), 163.563536m),
        (new(2002, 7, 1), 184.251937m),
        (new(2003, 1, 1), 225.999000m),
        (new(2004, 1, 1), 303.079500m),
        (new(2004, 7, 1), 318.233475m),
        (new(2005, 1, 1), 350.15m),
        (new(2006, 1, 1), 380.46m),
        (new(2007, 1, 1), 403.03m),
        (new(2007, 7, 1), 419.15m),
        (new(2008, 1, 1), 481.55m),
        (new(2008, 7, 1), 503.26m),
        (new(2009, 1, 1), 527.13m),
        (new(2009, 7, 1), 546.48m),
        (new(2010, 1, 1), 576.57m),
        (new(2010, 7, 1), 599.12m),
        (new(2011, 1, 1), 629.96m),
        (new(2011, 7, 1), 658.95m),
        (new(2012, 1, 1), 701.13m),
        (new(2012, 7, 1), 739.79m),
        (new(2013, 1, 1), 773.01m),
        (new(2013, 7, 1), 803.68m),
        (new(2014, 1, 1), 846.00m),
        (new(2014, 7, 1), 891.03m),
        (new(2015, 1, 1), 949.07m),
        (new(2015, 7, 1), 1000.54m),
        (new(2016, 1, 1), 1300.99m),
        (new(2017, 1, 1), 1404.06m),
        (new(2018, 1, 1), 1603.12m),
        (new(2019, 1, 1), 2020.90m),
        (new(2020, 1, 1), 2324.71m),
        (new(2021, 1, 1), 2825.90m),
        (new(2022, 1, 1), 4253.40m),
        (new(2022, 7, 1), 5500.35m),
        (new(2023, 1, 1), 8506.80m),
        (new(2023, 7, 1), 11402.32m),
        (new(2024, 1, 1), 17002.12m),
        (new(2025, 1, 1), 22104.67m),
        (new(2026, 1, 1), 30000.00m),
    ];

    /// <summary>Geriye dönük uyumluluk — yılın ilk dönemindeki net ücret.</summary>
    public static decimal Get(int year) => Get(new DateTime(year, 1, 1));

    public static decimal Get(DateTime date)
    {
        var d = date.Date;
        decimal wage = Periods[0].NetYtl;
        foreach (var (from, net) in Periods)
        {
            if (from > d) break;
            wage = net;
        }

        return wage;
    }
}
