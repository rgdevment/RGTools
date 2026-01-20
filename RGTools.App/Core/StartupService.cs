using System.Diagnostics;

namespace RGTools.App.Core;

public static class StartupService
{
    private const string TaskName = "RGToolsLauncher";

    public static void SetStartup(bool enable)
    {
        try
        {
            if (enable)
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName
                    ?? throw new InvalidOperationException("Could not determine EXE path");

                // Create task with highest privileges to bypass UAC on next login
                string args = $"/create /tn \"{TaskName}\" /tr \"'{exePath}'\" /sc onlogon /rl highest /f";
                RunSchtasks(args);
            }
            else
            {
                RunSchtasks($"/delete /tn \"{TaskName}\" /f");
            }
        }
        catch (Exception ex)
        {
            LogService.Log("[STARTUP] Failed to modify startup task", ex);
        }
    }

    private static void RunSchtasks(string args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "schtasks",
            Arguments = args,
            CreateNoWindow = true,
            UseShellExecute = true,
            Verb = "runas" // Requires admin only once to register the task
        };

        Process.Start(startInfo)?.WaitForExit();
    }
}
