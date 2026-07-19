using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Quartz;

namespace SanalBorsa.Infrastructure.Jobs;

/// <summary>
/// Nightly 23:00 Turkey — runs TradingView sync.py --incremental
/// (fills from each stock's LatestDataDate through today with raw prices).
/// </summary>
[DisallowConcurrentExecution]
public class TradingViewPriceSyncJob : IJob
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<TradingViewPriceSyncJob> _logger;

    public TradingViewPriceSyncJob(
        IConfiguration configuration,
        ILogger<TradingViewPriceSyncJob> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var section = _configuration.GetSection("TvSync");
        if (!section.GetValue("Enabled", false))
        {
            _logger.LogInformation("TradingViewPriceSyncJob skipped — TvSync:Enabled=false");
            return;
        }

        var workDir = section["WorkingDirectory"];
        var python = section["PythonExecutable"] ?? "python3";
        var args = section["Arguments"] ?? "sync.py --incremental";

        if (string.IsNullOrWhiteSpace(workDir) || !Directory.Exists(workDir))
        {
            _logger.LogError(
                "TradingViewPriceSyncJob misconfigured — WorkingDirectory missing: {Dir}",
                workDir);
            return;
        }

        _logger.LogInformation(
            "TradingViewPriceSyncJob starting: {Python} {Args} (cwd={Cwd})",
            python, args, workDir);

        var psi = new ProcessStartInfo
        {
            FileName = python,
            Arguments = args,
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                _logger.LogInformation("[tv-sync] {Line}", e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                _logger.LogWarning("[tv-sync:err] {Line}", e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(context.CancellationToken);

        if (process.ExitCode != 0)
        {
            _logger.LogError("TradingViewPriceSyncJob failed with exit code {Code}", process.ExitCode);
            throw new JobExecutionException(
                new InvalidOperationException($"TV sync exited with code {process.ExitCode}"),
                refireImmediately: false);
        }

        _logger.LogInformation("TradingViewPriceSyncJob completed successfully");
    }
}
