using NSubstitute;
using RGTools.App.Core;
using Xunit;

namespace RGTools.Tests;

public class BoostModeServiceTests
{
    private static (BoostModeService mode, IPowerPlanService power, INotificationService notify) Build()
    {
        var power = Substitute.For<IPowerPlanService>();
        var notify = Substitute.For<INotificationService>();
        return (new BoostModeService(power, notify), power, notify);
    }

    [Fact]
    public async Task Activate_AppliesMaxPerformance()
    {
        var (mode, power, _) = Build();

        await mode.ActivateAsync();

        await power.Received(1).ApplyHighPerformanceAsync();
        await power.DidNotReceive().ApplyPowerSaverAsync();
    }

    [Fact]
    public async Task Deactivate_RestoresPreviousPlan()
    {
        var (mode, power, _) = Build();

        await mode.DeactivateAsync();

        await power.Received(1).RestoreAsync();
    }
}
