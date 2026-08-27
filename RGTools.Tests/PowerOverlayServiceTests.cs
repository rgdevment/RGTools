using NSubstitute;
using RGTools.App.Core;
using Xunit;

namespace RGTools.Tests;

public class PowerOverlayServiceTests
{
    private const string RecommendedGuid = "00000000-0000-0000-0000-000000000000";
    private const string EfficiencyGuid = "961cc777-2547-4f9d-8174-7d86181b8a7a";
    private const string PerformanceGuid = "ded574b5-45a0-4f42-8737-46345c09c238";
    private const string BalancedPlanGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";

    private static IProcessRunner Runner(int exitCode = 0)
    {
        var runner = Substitute.For<IProcessRunner>();
        runner.RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(exitCode);
        return runner;
    }

    [Theory]
    [InlineData(PowerOverlay.Recommended, RecommendedGuid)]
    [InlineData(PowerOverlay.BestEfficiency, EfficiencyGuid)]
    [InlineData(PowerOverlay.BestPerformance, PerformanceGuid)]
    public async Task Apply_UsesTheOverlayGuid(PowerOverlay overlay, string expectedGuid)
    {
        var runner = Runner();
        var service = new PowerOverlayService(runner);

        Assert.True(await service.ApplyAsync(overlay));
        await runner.Received(1).RunAsync("powercfg", $"/overlaysetactive {expectedGuid}", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Apply_ReportsFailureWhenPowercfgFails()
    {
        var service = new PowerOverlayService(Runner(exitCode: 1));

        Assert.False(await service.ApplyAsync(PowerOverlay.BestEfficiency));
    }

    [Fact]
    public async Task Migrate_ActivatesBalancedBeforeDeletingTheCustomPlans()
    {
        var runner = Runner();
        var service = new PowerOverlayService(runner);

        await service.MigrateToBaselineAsync();

        Received.InOrder(() =>
        {
            runner.RunAsync("powercfg", $"/setactive {BalancedPlanGuid}", Arg.Any<CancellationToken>());
            runner.RunAsync("powercfg", "-delete e9a42b02-d5df-448d-aa00-03f14749eb71", Arg.Any<CancellationToken>());
            runner.RunAsync("powercfg", "-delete e9a42b02-d5df-448d-aa00-03f14749eb70", Arg.Any<CancellationToken>());
            runner.RunAsync("powercfg", $"/overlaysetactive {RecommendedGuid}", Arg.Any<CancellationToken>());
        });
    }
}
