using System.Text.RegularExpressions;

namespace RGTools.App.Core;

public sealed partial class PowerPlanService : IPowerPlanService
{
    private const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
    private const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private const string UltimateTemplateGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    private const string UltimateTargetGuid = "e9a42b02-d5df-448d-aa00-03f14749eb70";

    private readonly IProcessRunner _runner;
    private readonly ISystemStateStore _store;

    public PowerPlanService(IProcessRunner runner, ISystemStateStore store)
    {
        _runner = runner;
        _store = store;
    }

    public async Task ApplyHighPerformanceAsync()
    {
        if (!_store.Exists(StateKeys.PowerScheme))
        {
            var current = await GetActiveSchemeAsync();
            if (!string.IsNullOrWhiteSpace(current))
                await _store.SaveAsync(StateKeys.PowerScheme, current);
            else
                LogService.Log("[POWER] Could not capture active scheme; restore will default to Balanced.");
        }

        var target = await ResolveBestSchemeAsync();
        if (await _runner.RunAsync("powercfg", $"/setactive {target}") != 0 && target != BalancedGuid)
            await _runner.RunAsync("powercfg", $"/setactive {BalancedGuid}");
    }

    private async Task<string> ResolveBestSchemeAsync()
    {
        var list = await _runner.RunPowerShellCaptureAsync("powercfg /list");

        if (string.IsNullOrWhiteSpace(list)) return UltimateTargetGuid;

        if (list.Contains(UltimateTargetGuid, StringComparison.OrdinalIgnoreCase)) return UltimateTargetGuid;
        if (list.Contains(UltimateTemplateGuid, StringComparison.OrdinalIgnoreCase)) return UltimateTemplateGuid;

        if (await _runner.RunAsync("powercfg", $"-duplicatescheme {UltimateTemplateGuid} {UltimateTargetGuid}") == 0)
            return UltimateTargetGuid;

        return list.Contains(HighPerformanceGuid, StringComparison.OrdinalIgnoreCase) ? HighPerformanceGuid : BalancedGuid;
    }

    public async Task RestoreAsync()
    {
        string target = BalancedGuid;

        if (_store.Exists(StateKeys.PowerScheme))
        {
            var saved = await _store.LoadAsync<string>(StateKeys.PowerScheme);
            if (!string.IsNullOrWhiteSpace(saved)) target = saved;
            _store.Clear(StateKeys.PowerScheme);
        }

        await _runner.RunAsync("powercfg", $"/setactive {target}");
    }

    private async Task<string?> GetActiveSchemeAsync()
    {
        var output = await _runner.RunPowerShellCaptureAsync("powercfg /getactivescheme");
        var match = GuidRegex().Match(output);
        return match.Success ? match.Value : null;
    }

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidRegex();
}
