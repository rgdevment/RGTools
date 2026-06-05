namespace RGTools.App.Core;

public sealed class StartupService : IStartupService
{
    private const string TaskName = "RGToolsLauncher";

    private readonly IProcessRunner _runner;

    public StartupService(IProcessRunner runner) => _runner = runner;

    public async Task<bool> SetStartupAsync(bool enable)
    {
        try
        {
            string args;
            if (enable)
            {
                string exePath = Environment.ProcessPath
                    ?? throw new InvalidOperationException("Could not determine executable path");
                args = $"/create /tn \"{TaskName}\" /tr \"\\\"{exePath}\\\"\" /sc onlogon /rl highest /f";
            }
            else
            {
                args = $"/delete /tn \"{TaskName}\" /f";
            }

            return await _runner.RunAsync("schtasks", args) == 0;
        }
        catch (Exception ex)
        {
            LogService.Log("[STARTUP] Failed to modify startup task", ex);
            return false;
        }
    }

    public async Task<bool?> IsEnabledAsync()
    {
        int exit = await _runner.RunAsync("schtasks", $"/query /tn \"{TaskName}\"");
        return exit switch
        {
            0 => true,
            -1 => null,
            _ => false
        };
    }
}
