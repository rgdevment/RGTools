using System.Diagnostics;

namespace RGTools.App.Core;

public sealed class WorkloadGuardService : IWorkloadGuard
{
    private const string DockerService = "com.docker.service";

    private static readonly string[] ProcessesToClose =
    {
        "Docker Desktop",
        "com.docker",
        "LM Studio",
        "Slack",
        "Discord",
        "Spark Desktop",
        "WhatsApp",
        "qbittorrent"
    };

    private readonly IProcessRunner _runner;

    public WorkloadGuardService(IProcessRunner runner) => _runner = runner;

    public async Task<WorkloadSnapshot> CaptureAsync(CancellationToken ct = default) => new()
    {
        WSearchWasRunning = await IsServiceRunningAsync("WSearch", ct).ConfigureAwait(false),
        DockerServiceWasRunning = await IsServiceRunningAsync(DockerService, ct).ConfigureAwait(false)
    };

    public async Task SuspendAsync(CancellationToken ct = default)
    {
        string nameList = string.Join(",", ProcessesToClose.Select(n => $"'{n}*'"));

        await _runner.RunPowerShellAsync(
            "Stop-Service -Name 'WSearch' -Force -ErrorAction SilentlyContinue; " +
            $"Stop-Service -Name '{DockerService}' -Force -ErrorAction SilentlyContinue; " +
            $"$p = Get-Process {nameList} -ErrorAction SilentlyContinue; " +
            "$p | ForEach-Object { $_.CloseMainWindow() | Out-Null }; " +
            "Start-Sleep -Seconds 3; " +
            $"Get-Process {nameList} -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue; " +
            "wsl --shutdown", ct).ConfigureAwait(false);

        LogService.Log($"[WORKLOAD] Suspended (graceful-then-force: {string.Join(", ", ProcessesToClose)}, WSL2)");
    }

    public async Task RestoreAsync(WorkloadSnapshot snapshot, CancellationToken ct = default)
    {
        if (snapshot.WSearchWasRunning)
            await _runner.RunPowerShellAsync("Start-Service -Name 'WSearch' -ErrorAction SilentlyContinue", ct).ConfigureAwait(false);

        if (snapshot.DockerServiceWasRunning)
            await _runner.RunPowerShellAsync($"Start-Service -Name '{DockerService}' -ErrorAction SilentlyContinue", ct).ConfigureAwait(false);

        LogService.Log("[WORKLOAD] Restored WSearch/Docker service. Closed apps (incl. Docker Desktop) must be reopened manually.");
    }

    private async Task<bool> IsServiceRunningAsync(string service, CancellationToken ct)
    {
        var output = await _runner.RunPowerShellCaptureAsync(
            $"(Get-Service -Name '{service}' -ErrorAction SilentlyContinue).Status", ct).ConfigureAwait(false);
        return output.Contains("Running", StringComparison.OrdinalIgnoreCase);
    }
}
