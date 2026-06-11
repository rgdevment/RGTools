using System.Text.RegularExpressions;

namespace RGTools.App.Core;

public sealed partial class PowerPlanService : IPowerPlanService
{
    private const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
    private const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private const string UltimateTemplateGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    private const string UltimateTargetGuid = "e9a42b02-d5df-448d-aa00-03f14749eb70";
    private const string SaverTargetGuid = "e9a42b02-d5df-448d-aa00-03f14749eb71";
    private const string SaverName = "RGTools Power Saver";
    private const string SaverDescription = "Plan de ahorro afinado gestionado por RGTools.";

    private const string SubProcessor = "54533251-82be-4824-96c1-47b60b740d00";
    private const string SubPciExpress = "501a4d13-42af-4429-9fd1-a8218c268e20";
    private const string SubDisk = "0012ee47-9041-4b5d-9b77-535fba8b1442";

    private const string SetProcMax = "bc5038f7-23e0-4960-96da-33abaf5935ec";
    private const string SetProcMin = "893dee8e-2bef-41e0-89c6-b55d0929964c";
    private const string SetProcBoost = "be337238-0d82-4146-a960-4f3749d470c7";
    private const string SetCoreParkingMin = "0cc5b647-c1df-4637-891a-dec35c318583";
    private const string SetPciAspm = "ee12f906-d277-404b-b6da-e5fa1a576df5";
    private const string SetDiskIdle = "6738e2c4-e8a5-4a42-b16a-e040e769756e";

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
            var current = await GetActiveSchemeAsync().ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(current))
                await _store.SaveAsync(StateKeys.PowerScheme, current).ConfigureAwait(false);
            else
                LogService.Log("[POWER] Could not capture active scheme; restore will default to Balanced.");
        }

        var target = await ResolveBestSchemeAsync().ConfigureAwait(false);
        if (await _runner.RunAsync("powercfg", $"/setactive {target}").ConfigureAwait(false) != 0 && target != BalancedGuid)
            await _runner.RunAsync("powercfg", $"/setactive {BalancedGuid}").ConfigureAwait(false);
    }

    public async Task ApplyPowerSaverAsync()
    {
        // Work imposes its own saver plan; any scheme captured by Gaming is no longer needed.
        if (_store.Exists(StateKeys.PowerScheme))
            _store.Clear(StateKeys.PowerScheme);

        var guid = await ResolveSaverSchemeAsync().ConfigureAwait(false);
        await TuneSaverSchemeAsync(guid).ConfigureAwait(false);
        await _runner.RunAsync("powercfg", $"/setactive {guid}").ConfigureAwait(false);
    }

    private async Task<string> ResolveSaverSchemeAsync()
    {
        var list = await _runner.RunPowerShellCaptureAsync("powercfg /list").ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(list) && list.Contains(SaverTargetGuid, StringComparison.OrdinalIgnoreCase))
            return SaverTargetGuid;

        if (await _runner.RunAsync("powercfg", $"-duplicatescheme {BalancedGuid} {SaverTargetGuid}").ConfigureAwait(false) == 0)
        {
            await _runner.RunAsync("powercfg", $"-changename {SaverTargetGuid} \"{SaverName}\" \"{SaverDescription}\"").ConfigureAwait(false);
            return SaverTargetGuid;
        }

        return BalancedGuid;
    }

    private async Task TuneSaverSchemeAsync(string guid)
    {
        // AC-only: the target is a desktop. ProcMax 99 disables Turbo Boost (caps at base clock) — most of the
        // wattage/heat savings with no impact on light dev work; only heavy all-core builds run slightly slower.
        // Do not drop ProcMax below 99: that throttles the base clock and is noticeable in everyday use.
        (string sub, string setting, int value)[] tweaks =
        {
            (SubProcessor, SetProcMax, 99),
            (SubProcessor, SetProcMin, 5),
            (SubProcessor, SetProcBoost, 1),
            (SubProcessor, SetCoreParkingMin, 50),
            (SubPciExpress, SetPciAspm, 2),
            (SubDisk, SetDiskIdle, 600),
        };

        foreach (var (sub, setting, value) in tweaks)
            await _runner.RunAsync("powercfg", $"/setacvalueindex {guid} {sub} {setting} {value}").ConfigureAwait(false);
    }

    private async Task<string> ResolveBestSchemeAsync()
    {
        var list = await _runner.RunPowerShellCaptureAsync("powercfg /list").ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(list)) return UltimateTargetGuid;

        if (list.Contains(UltimateTargetGuid, StringComparison.OrdinalIgnoreCase)) return UltimateTargetGuid;
        if (list.Contains(UltimateTemplateGuid, StringComparison.OrdinalIgnoreCase)) return UltimateTemplateGuid;

        if (await _runner.RunAsync("powercfg", $"-duplicatescheme {UltimateTemplateGuid} {UltimateTargetGuid}").ConfigureAwait(false) == 0)
            return UltimateTargetGuid;

        return list.Contains(HighPerformanceGuid, StringComparison.OrdinalIgnoreCase) ? HighPerformanceGuid : BalancedGuid;
    }

    public async Task RestoreAsync()
    {
        string target = BalancedGuid;

        if (_store.Exists(StateKeys.PowerScheme))
        {
            var saved = await _store.LoadAsync<string>(StateKeys.PowerScheme).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(saved)) target = saved;
            _store.Clear(StateKeys.PowerScheme);
        }

        await _runner.RunAsync("powercfg", $"/setactive {target}").ConfigureAwait(false);
    }

    private async Task<string?> GetActiveSchemeAsync()
    {
        var output = await _runner.RunPowerShellCaptureAsync("powercfg /getactivescheme").ConfigureAwait(false);
        var match = GuidRegex().Match(output);
        return match.Success ? match.Value : null;
    }

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidRegex();
}
