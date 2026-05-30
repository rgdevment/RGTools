using System.Diagnostics;

namespace RGTools.App.Core;

public sealed class WorkloadGuardService : IWorkloadGuard
{
    private static readonly string[] ProcessesToClose =
    {
        "Docker Desktop",
        "LM Studio",
        "Slack",
        "Discord",
        "Spark Desktop",
        "WhatsApp",
        "qbittorrent"
    };

    private readonly IProcessRunner _runner;

    public WorkloadGuardService(IProcessRunner runner) => _runner = runner;

    public async Task<WorkloadSnapshot> SuspendAsync(CancellationToken ct = default)
    {
        var snapshot = new WorkloadSnapshot
        {
            WSearchWasRunning = await IsServiceRunningAsync("WSearch", ct)
        };

        string nameList = string.Join(",", ProcessesToClose.Select(n => $"'{n}*'"));

        await _runner.RunPowerShellAsync(
            "Stop-Service -Name 'WSearch' -Force -ErrorAction SilentlyContinue; " +
            $"$p = Get-Process {nameList} -ErrorAction SilentlyContinue; " +
            "$p | ForEach-Object { $_.CloseMainWindow() | Out-Null }; " +
            "Start-Sleep -Seconds 3; " +
            $"Get-Process {nameList} -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue; " +
            "wsl --shutdown", ct);

        LogService.Log($"[WORKLOAD] Suspended (WSearch={snapshot.WSearchWasRunning}, graceful-then-force: {string.Join(", ", ProcessesToClose)}, WSL2)");
        return snapshot;
    }

    public async Task RestoreAsync(WorkloadSnapshot snapshot, CancellationToken ct = default)
    {
        if (snapshot.WSearchWasRunning)
            await _runner.RunPowerShellAsync("Start-Service -Name 'WSearch' -ErrorAction SilentlyContinue", ct);

        LogService.Log("[WORKLOAD] Restored WSearch. Las apps cerradas se reabren manualmente.");
    }

    private async Task<bool> IsServiceRunningAsync(string service, CancellationToken ct)
    {
        var output = await _runner.RunPowerShellCaptureAsync(
            $"(Get-Service -Name '{service}' -ErrorAction SilentlyContinue).Status", ct);
        return output.Contains("Running", StringComparison.OrdinalIgnoreCase);
    }
}
