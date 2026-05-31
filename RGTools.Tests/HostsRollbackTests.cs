using NSubstitute;
using RGTools.App.Core;
using Xunit;

namespace RGTools.Tests;

public class HostsRollbackTests
{
    private static (WorkModeService work, IHostsBlocker hosts, ISystemStateStore store) Build()
    {
        var hosts = Substitute.For<IHostsBlocker>();
        hosts.RestoreAsync().Returns(Task.CompletedTask);

        var store = Substitute.For<ISystemStateStore>();
        store.Exists(Arg.Any<string>()).Returns(false);

        var workload = Substitute.For<IWorkloadGuard>();
        var gpu = Substitute.For<IGpuPriorityService>();
        var silencer = Substitute.For<INotificationSilencer>();
        var power = Substitute.For<IPowerPlanService>();
        var notify = Substitute.For<INotificationService>();

        var work = new WorkModeService(workload, gpu, silencer, hosts, power, store, notify);
        return (work, hosts, store);
    }

    [Fact]
    public async Task WorkActivate_RestoresHosts()
    {
        var (work, hosts, _) = Build();

        await work.ActivateAsync();

        await hosts.Received(1).RestoreAsync();
    }

    [Fact]
    public async Task WorkActivate_RestoresGpuSilencerPowerAndHosts()
    {
        var hosts = Substitute.For<IHostsBlocker>();
        var gpu = Substitute.For<IGpuPriorityService>();
        var silencer = Substitute.For<INotificationSilencer>();
        var power = Substitute.For<IPowerPlanService>();
        var store = Substitute.For<ISystemStateStore>();
        store.Exists(Arg.Any<string>()).Returns(false);
        var work = new WorkModeService(
            Substitute.For<IWorkloadGuard>(), gpu, silencer, hosts, power, store,
            Substitute.For<INotificationService>());

        await work.ActivateAsync();

        await gpu.Received(1).RestoreAsync();
        await silencer.Received(1).RestoreAsync();
        await power.Received(1).RestoreAsync();
        await hosts.Received(1).RestoreAsync();
    }
}
