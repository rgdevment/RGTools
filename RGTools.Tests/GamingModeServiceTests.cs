using NSubstitute;
using RGTools.App.Core;
using Xunit;

namespace RGTools.Tests;

public class GamingModeServiceTests
{
    private sealed class Harness
    {
        public IWorkloadGuard Workload = Substitute.For<IWorkloadGuard>();
        public IPowerPlanService Power = Substitute.For<IPowerPlanService>();
        public IGpuPriorityService Gpu = Substitute.For<IGpuPriorityService>();
        public IDisplayRefreshService Display = Substitute.For<IDisplayRefreshService>();
        public IGamingTweaksService Tweaks = Substitute.For<IGamingTweaksService>();
        public INotificationSilencer Silencer = Substitute.For<INotificationSilencer>();
        public ISystemStateStore Store = Substitute.For<ISystemStateStore>();
        public IUserConsentService Consent = Substitute.For<IUserConsentService>();
        public INotificationService Notify = Substitute.For<INotificationService>();

        public GamingModeService Build()
        {
            Workload.CaptureAsync(Arg.Any<CancellationToken>()).Returns(new WorkloadSnapshot());
            return new GamingModeService(Workload, Power, Gpu, Display, Tweaks, Silencer, Store, Consent, Notify);
        }
    }

    [Fact]
    public async Task Activate_AppliesPerformanceDisplayAndTweaks()
    {
        var h = new Harness();
        h.Store.Exists(Arg.Any<string>()).Returns(false);
        h.Consent.RequestAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        var gaming = h.Build();

        await gaming.ActivateAsync();

        await h.Power.Received(1).ApplyHighPerformanceAsync();
        await h.Silencer.Received(1).SilenceAsync();
        await h.Display.Received(1).ApplyMaxAsync();
        await h.Tweaks.Received(1).ApplyAsync();
    }

    [Fact]
    public async Task Activate_CapturesThenSuspendsWorkloadWhenNoSnapshotExists()
    {
        var h = new Harness();
        h.Store.Exists(StateKeys.Workload).Returns(false);
        h.Consent.RequestAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        var gaming = h.Build();

        await gaming.ActivateAsync();

        Received.InOrder(() =>
        {
            h.Workload.CaptureAsync(Arg.Any<CancellationToken>());
            h.Store.SaveAsync(StateKeys.Workload, Arg.Any<WorkloadSnapshot>());
            h.Workload.SuspendAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Activate_AppliesGpuOnlyWithConsent()
    {
        var h = new Harness();
        h.Store.Exists(Arg.Any<string>()).Returns(false);
        h.Consent.RequestAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        var gaming = h.Build();

        await gaming.ActivateAsync();

        await h.Gpu.Received(1).ApplyAsync();
    }

    [Fact]
    public async Task Deactivate_RestoresDisplayAndTweaks()
    {
        var h = new Harness();
        var gaming = h.Build();

        await gaming.DeactivateAsync();

        await h.Display.Received(1).RestoreAsync();
        await h.Tweaks.Received(1).RestoreAsync();
        await h.Gpu.Received(1).RestoreAsync();
        await h.Power.Received(1).RestoreAsync();
    }
}
