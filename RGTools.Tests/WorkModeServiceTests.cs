using NSubstitute;
using RGTools.App.Core;
using Xunit;

namespace RGTools.Tests;

public class WorkModeServiceTests
{
    [Fact]
    public async Task Activate_ContinuesRestoreEvenIfOneStepThrows()
    {
        var workload = Substitute.For<IWorkloadGuard>();
        var gpu = Substitute.For<IGpuPriorityService>();
        var display = Substitute.For<IDisplayRefreshService>();
        var tweaks = Substitute.For<IGamingTweaksService>();
        var silencer = Substitute.For<INotificationSilencer>();
        var power = Substitute.For<IPowerPlanService>();
        var store = Substitute.For<ISystemStateStore>();
        var notify = Substitute.For<INotificationService>();

        store.Exists(Arg.Any<string>()).Returns(false);
        gpu.RestoreAsync().Returns(Task.FromException(new InvalidOperationException("boom")));

        var work = new WorkModeService(workload, gpu, display, tweaks, silencer, power, store, notify);

        await work.ActivateAsync();

        await display.Received(1).RestoreAsync();
        await tweaks.Received(1).RestoreAsync();
        await silencer.Received(1).RestoreAsync();
        await power.Received(1).ApplyPowerSaverAsync();
    }
}
