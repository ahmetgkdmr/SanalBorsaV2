namespace SanalBorsa.Application.Common.Constants;

public static class MinimumWageByYear
{
    public static readonly IReadOnlyDictionary<int, decimal> Values = new Dictionary<int, decimal>
    {
        [2010] = 599, [2011] = 658, [2012] = 739, [2013] = 803, [2014] = 891, [2015] = 1000,
        [2016] = 1300, [2017] = 1404, [2018] = 1603, [2019] = 2020, [2020] = 2324, [2021] = 2825,
        [2022] = 4900, [2023] = 10100, [2024] = 17002, [2025] = 22104, [2026] = 30000,
    };

    public static decimal Get(int year) =>
        Values.TryGetValue(year, out var wage) ? wage : Values[2026];
}
