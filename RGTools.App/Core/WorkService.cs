using System.Diagnostics;

namespace RGTools.App.Core;

public class WorkService
{
    private readonly VpnService _vpnService;

    public WorkService(VpnService vpnService)
    {
        _vpnService = vpnService;
    }

    public async Task SwitchToWorkOffAsync()
    {
        LogService.Log("[WorkOff] Starting system cleanup sequence");

        try
        {
            if (_vpnService.IsActive)
            {
                LogService.Log("[WorkOff] Active VPN detected, initiating shutdown");
                await _vpnService.ToggleAsync();
            }
        }
        catch (Exception ex)
        {
            LogService.Log($"[WorkOff] Critical error during VPN shutdown: {ex.Message}");
        }

        await Task.Run(() =>
        {
            try
            {
                LogService.Log("[WorkOff] Executing nuclear shutdown: WSL2, LM Studio, Docker Desktop");

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -Command \"wsl --shutdown; Get-Process 'LM Studio', 'Docker Desktop' -ErrorAction SilentlyContinue | Stop-Process -Force\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                using var process = Process.Start(psi);
                process?.WaitForExit();

                if (process?.ExitCode != 0)
                {
                    LogService.Log($"[WorkOff] Cleanup command finished with exit code: {process?.ExitCode}");
                }

                LogService.Log("[WorkOff] Resource cleanup sequence completed");
            }
            catch (Exception ex)
            {
                LogService.Log($"[WorkOff] Error during resource cleanup: {ex.Message}");
                throw;
            }
        });
    }
}
