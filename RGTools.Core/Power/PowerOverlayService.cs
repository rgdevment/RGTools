using Microsoft.Win32;

namespace RGTools.App.Core;

public sealed class PowerOverlayService : IPowerOverlayService
{
    private const string SchemesKey = @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes";
    private const string ActiveOverlayValue = "ActiveOverlayAcPowerScheme";

    private const string RecommendedGuid = "00000000-0000-0000-0000-000000000000";
    private const string EfficiencyGuid = "961cc777-2547-4f9d-8174-7d86181b8a7a";
    private const string PerformanceGuid = "ded574b5-45a0-4f42-8737-46345c09c238";

    private const string BalancedPlanGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";

    // Fixed GUIDs the pre-overlay versions of RGTools used for the plans it created.
    private const string LegacySaverPlanGuid = "e9a42b02-d5df-448d-aa00-03f14749eb71";
    private const string LegacyUltimatePlanGuid = "e9a42b02-d5df-448d-aa00-03f14749eb70";

    private readonly IProcessRunner _runner;

    public PowerOverlayService(IProcessRunner runner) => _runner = runner;

    public PowerOverlay ReadActive()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(SchemesKey, writable: false);
            string? guid = key?.GetValue(ActiveOverlayValue) as string;

            return guid?.Trim().ToLowerInvariant() switch
            {
                EfficiencyGuid => PowerOverlay.BestEfficiency,
                PerformanceGuid => PowerOverlay.BestPerformance,
                // Absent value means the overlay was never touched, which Windows treats as Recommended.
                _ => PowerOverlay.Recommended
            };
        }
        catch (Exception ex)
        {
            LogService.Log("[POWER] Could not read active overlay", ex);
            return PowerOverlay.Recommended;
        }
    }

    public async Task<bool> ApplyAsync(PowerOverlay overlay)
    {
        // /overlaysetactive is undocumented (absent from powercfg /?) but present since Windows 10 1809
        // and is the only way to drive the Power mode slider that Settings exposes.
        int exit = await _runner.RunAsync("powercfg", $"/overlaysetactive {GuidFor(overlay)}").ConfigureAwait(false);

        if (exit != 0)
        {
            LogService.Log($"[POWER] Overlay {overlay} failed (exit {exit}).");
            return false;
        }

        LogService.Log($"[POWER] Overlay set to {overlay}.");
        return true;
    }

    public async Task MigrateToBaselineAsync()
    {
        // -delete refuses to remove the active plan, so hand the system back to Balanced first.
        await _runner.RunAsync("powercfg", $"/setactive {BalancedPlanGuid}").ConfigureAwait(false);

        foreach (var plan in new[] { LegacySaverPlanGuid, LegacyUltimatePlanGuid })
            await _runner.RunAsync("powercfg", $"-delete {plan}").ConfigureAwait(false);

        await ApplyAsync(PowerOverlay.Recommended).ConfigureAwait(false);

        LogService.Log("[POWER] Migrated to stock Balanced plan; custom RGTools plans removed.");
    }

    private static string GuidFor(PowerOverlay overlay) => overlay switch
    {
        PowerOverlay.BestEfficiency => EfficiencyGuid,
        PowerOverlay.BestPerformance => PerformanceGuid,
        _ => RecommendedGuid
    };
}
