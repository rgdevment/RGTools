namespace RGTools.App.Core;

public sealed class PowerPlanService : IPowerPlanService
{
    private const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
    private const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private const string UltimatePerformanceGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";

    private readonly IProcessRunner _runner;

    public PowerPlanService(IProcessRunner runner) => _runner = runner;

    public Task SetBalancedAsync() => _runner.RunAsync("powercfg", $"/setactive {BalancedGuid}");

    public async Task SetHighPerformanceAsync()
    {
        await _runner.RunAsync("powercfg", $"/duplicatescheme {UltimatePerformanceGuid}");

        if (await _runner.RunAsync("powercfg", $"/setactive {UltimatePerformanceGuid}") != 0)
            await _runner.RunAsync("powercfg", $"/setactive {HighPerformanceGuid}");
    }
}
