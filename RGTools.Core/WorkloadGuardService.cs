namespace RGTools.App.Core;

public sealed class WorkloadGuardService : IWorkloadGuard
{
    private const string DockerService = "com.docker.service";

    // Noisy on CPU but cheap to keep resident: EcoQoS parks them without costing you the session.
    private static readonly string[] Throttled =
    {
        "Slack",
        "Discord",
        "WhatsApp",
        "Spark Desktop",
        "SearchIndexer"
    };

    // Closed instead of throttled: EcoQoS does not hand back the RAM and GPU memory these hold.
    private static readonly string[] ToClose =
    {
        "Docker Desktop",
        "com.docker",
        "LM Studio",
        "qbittorrent"
    };

    private readonly IProcessRunner _runner;
    private readonly IProcessThrottler _throttler;

    public WorkloadGuardService(IProcessRunner runner, IProcessThrottler throttler)
    {
        _runner = runner;
        _throttler = throttler;
    }

    public async Task<WorkloadSnapshot> CaptureAsync(CancellationToken ct = default) => new()
    {
        DockerServiceWasRunning = await IsServiceRunningAsync(DockerService, ct).ConfigureAwait(false)
    };

    public async Task SuspendAsync(CancellationToken ct = default)
    {
        Throttle(enabled: true);

        // Escape single quotes so a process name can't break out of the PowerShell string literal.
        string nameList = string.Join(",", ToClose.Select(n => $"'{n.Replace("'", "''")}*'"));

        await _runner.RunPowerShellAsync(
            $"Stop-Service -Name '{DockerService}' -Force -ErrorAction SilentlyContinue; " +
            $"$p = Get-Process {nameList} -ErrorAction SilentlyContinue; " +
            "$p | ForEach-Object { $_.CloseMainWindow() | Out-Null }; " +
            "Start-Sleep -Seconds 3; " +
            $"Get-Process {nameList} -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue; " +
            "wsl --shutdown", ct).ConfigureAwait(false);

        LogService.Log($"[WORKLOAD] Closed: {string.Join(", ", ToClose)}, WSL2.");
    }

    public async Task RestoreAsync(WorkloadSnapshot? snapshot, CancellationToken ct = default)
    {
        Throttle(enabled: false);

        if (snapshot?.DockerServiceWasRunning == true)
            await _runner.RunPowerShellAsync(
                $"Start-Service -Name '{DockerService}' -ErrorAction SilentlyContinue", ct).ConfigureAwait(false);

        LogService.Log("[WORKLOAD] Efficiency mode lifted. Closed apps must be reopened manually.");
    }

    private void Throttle(bool enabled)
    {
        foreach (var name in Throttled)
            _throttler.SetEfficiency(name, enabled);
    }

    private async Task<bool> IsServiceRunningAsync(string service, CancellationToken ct)
    {
        var output = await _runner.RunPowerShellCaptureAsync(
            $"(Get-Service -Name '{service}' -ErrorAction SilentlyContinue).Status", ct).ConfigureAwait(false);
        return output.Contains("Running", StringComparison.OrdinalIgnoreCase);
    }
}
