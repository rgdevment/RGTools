using System.Text.RegularExpressions;

namespace RGTools.App.Core;

public sealed partial class PowerPlanService : IPowerPlanService
{
    private const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
    private const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

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
        }

        await _runner.RunAsync("powercfg", $"/setactive {HighPerformanceGuid}");
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
