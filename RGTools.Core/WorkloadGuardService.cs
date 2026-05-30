using System.Diagnostics;

namespace RGTools.App.Core;

public sealed class WorkloadGuardService : IWorkloadGuard
{
    private readonly IProcessRunner _runner;

    public WorkloadGuardService(IProcessRunner runner) => _runner = runner;

    public async Task<WorkloadSnapshot> SuspendAsync(CancellationToken ct = default)
    {
        var snapshot = new WorkloadSnapshot
        {
            WSearchWasRunning = await IsServiceRunningAsync("WSearch", ct),
            DockerWasRunning = IsProcessRunning("Docker Desktop"),
            LmStudioWasRunning = IsProcessRunning("LM Studio")
        };

        await _runner.RunPowerShellAsync(
            "Stop-Service -Name 'WSearch' -Force -ErrorAction SilentlyContinue; " +
            "Get-Process 'Docker Desktop','LM Studio' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue; " +
            "wsl --shutdown", ct);

        LogService.Log($"[WORKLOAD] Suspended (WSearch={snapshot.WSearchWasRunning}, Docker={snapshot.DockerWasRunning}, LM={snapshot.LmStudioWasRunning})");
        return snapshot;
    }

    public async Task RestoreAsync(WorkloadSnapshot snapshot, CancellationToken ct = default)
    {
        if (snapshot.WSearchWasRunning)
            await _runner.RunPowerShellAsync("Start-Service -Name 'WSearch' -ErrorAction SilentlyContinue", ct);

        LogService.Log("[WORKLOAD] Restored WSearch service. Docker/WSL2/LM Studio se reabren manualmente.");
    }

    private async Task<bool> IsServiceRunningAsync(string service, CancellationToken ct)
    {
        var output = await _runner.RunPowerShellCaptureAsync(
            $"(Get-Service -Name '{service}' -ErrorAction SilentlyContinue).Status", ct);
        return output.Contains("Running", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProcessRunning(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }
    }
}
