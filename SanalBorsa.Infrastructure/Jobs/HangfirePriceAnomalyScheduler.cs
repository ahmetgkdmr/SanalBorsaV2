using Hangfire;
using SanalBorsa.Application.Common.Interfaces;

namespace SanalBorsa.Infrastructure.Jobs;

/// <summary>Hangfire delayed job ile <see cref="IPriceAnomalyScheduler"/> uygulaması.</summary>
public sealed class HangfirePriceAnomalyScheduler : IPriceAnomalyScheduler
{
    private readonly IBackgroundJobClient _jobs;

    public HangfirePriceAnomalyScheduler(IBackgroundJobClient jobs) => _jobs = jobs;

    public void ScheduleRecheck(string symbol, DateTime date, decimal previousClose, TimeSpan delay)
        => _jobs.Schedule<PriceAnomalyRecheckJob>(
            j => j.RecheckAsync(symbol, date, previousClose, CancellationToken.None),
            delay);
}
