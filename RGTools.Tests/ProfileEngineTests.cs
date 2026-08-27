using NSubstitute;
using RGTools.App.Core;
using Xunit;

namespace RGTools.Tests;

public class ProfileEngineTests
{
    private sealed class Harness
    {
        public IPowerOverlayService Overlay = Substitute.For<IPowerOverlayService>();
        public IWorkloadGuard Workload = Substitute.For<IWorkloadGuard>();
        public IGamingTweaksService Tweaks = Substitute.For<IGamingTweaksService>();
        public INotificationSilencer Silencer = Substitute.For<INotificationSilencer>();
        public IGpuPriorityService Gpu = Substitute.For<IGpuPriorityService>();
        public IUserConsentService Consent = Substitute.For<IUserConsentService>();
        public IConfigService Config = Substitute.For<IConfigService>();
        public ISystemStateStore Store = Substitute.For<ISystemStateStore>();
        public INotificationService Notify = Substitute.For<INotificationService>();

        public Harness(ProfileKind initial = ProfileKind.Balanced, params string[] existingKeys)
        {
            var keys = new HashSet<string>(existingKeys);
            Config.Current.Returns(new AppSettings { ActiveProfile = initial });
            Config.UpdateAsync(Arg.Any<Func<AppSettings, AppSettings>>()).Returns(Task.CompletedTask);
            Store.Exists(Arg.Any<string>()).Returns(ci => keys.Contains((string)ci[0]));
            Store.SaveAsync(Arg.Any<string>(), Arg.Any<object>()).Returns(Task.CompletedTask);
            Workload.CaptureAsync(Arg.Any<CancellationToken>()).Returns(new WorkloadSnapshot());
            Consent.RequestAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        }

        public ProfileEngine Build() =>
            new(Overlay, Workload, Tweaks, Silencer, Gpu, Consent, Config, Store, Notify);
    }

    [Fact]
    public async Task Balanced_LeavesEveryLayerRestored()
    {
        var h = new Harness();
        var engine = h.Build();

        await engine.ApplyAsync(ProfileKind.Balanced);

        await h.Overlay.Received(1).ApplyAsync(PowerOverlay.Recommended);
        await h.Tweaks.Received(1).RestoreAsync();
        await h.Silencer.Received(1).RestoreAsync();
        await h.Gpu.Received(1).RestoreAsync();
        await h.Workload.Received(1).RestoreAsync(Arg.Any<WorkloadSnapshot?>(), Arg.Any<CancellationToken>());
        await h.Tweaks.DidNotReceive().ApplyAsync();
    }

    [Fact]
    public async Task Work_UsesEfficiencyOverlay_AndRestoresGamingLayers()
    {
        var h = new Harness();
        var engine = h.Build();

        await engine.ApplyAsync(ProfileKind.Work);

        await h.Overlay.Received(1).ApplyAsync(PowerOverlay.BestEfficiency);
        await h.Tweaks.Received(1).RestoreAsync();
        await h.Silencer.Received(1).RestoreAsync();
        await h.Workload.Received(1).RestoreAsync(Arg.Any<WorkloadSnapshot?>(), Arg.Any<CancellationToken>());
        await h.Workload.DidNotReceive().SuspendAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Gaming_AppliesEveryLayer()
    {
        var h = new Harness();
        var engine = h.Build();

        await engine.ApplyAsync(ProfileKind.Gaming);

        await h.Overlay.Received(1).ApplyAsync(PowerOverlay.BestPerformance);
        await h.Tweaks.Received(1).ApplyAsync();
        await h.Silencer.Received(1).SilenceAsync();
        await h.Gpu.Received(1).ApplyAsync();
        await h.Workload.Received(1).SuspendAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Gaming_WithoutConsent_RestoresGpuInstead()
    {
        var h = new Harness();
        h.Consent.RequestAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        var engine = h.Build();

        await engine.ApplyAsync(ProfileKind.Gaming);

        await h.Gpu.DidNotReceive().ApplyAsync();
        await h.Gpu.Received(1).RestoreAsync();
    }

    [Fact]
    public async Task ApplyingTheActiveProfileAgain_Reapplies()
    {
        var h = new Harness(ProfileKind.Work);
        var engine = h.Build();

        await engine.ApplyAsync(ProfileKind.Work);
        await engine.ApplyAsync(ProfileKind.Work);

        await h.Overlay.Received(2).ApplyAsync(PowerOverlay.BestEfficiency);
    }

    [Fact]
    public async Task Gaming_CapturesWorkloadSnapshotOnlyWhenAbsent()
    {
        var h = new Harness(ProfileKind.Balanced, StateKeys.Workload);
        var engine = h.Build();

        await engine.ApplyAsync(ProfileKind.Gaming);

        await h.Workload.DidNotReceive().CaptureAsync(Arg.Any<CancellationToken>());
        await h.Store.DidNotReceive().SaveAsync(StateKeys.Workload, Arg.Any<WorkloadSnapshot>());
        await h.Workload.Received(1).SuspendAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Gaming_CapturesBeforeSuspendingWhenNoSnapshotExists()
    {
        var h = new Harness();
        var engine = h.Build();

        await engine.ApplyAsync(ProfileKind.Gaming);

        Received.InOrder(() =>
        {
            h.Workload.CaptureAsync(Arg.Any<CancellationToken>());
            h.Store.SaveAsync(StateKeys.Workload, Arg.Any<WorkloadSnapshot>());
            h.Workload.SuspendAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task ALayerThrowing_DoesNotStopTheRest()
    {
        var h = new Harness();
        h.Tweaks.RestoreAsync().Returns(Task.FromException(new InvalidOperationException("layer down")));
        var engine = h.Build();

        await engine.ApplyAsync(ProfileKind.Work);

        await h.Silencer.Received(1).RestoreAsync();
        await h.Gpu.Received(1).RestoreAsync();
        await h.Workload.Received(1).RestoreAsync(Arg.Any<WorkloadSnapshot?>(), Arg.Any<CancellationToken>());
        Assert.Equal(ProfileKind.Work, engine.Active);
    }

    [Fact]
    public async Task PersistsIntent_BeforeTouchingTheSystem()
    {
        var h = new Harness();
        var engine = h.Build();

        await engine.ApplyAsync(ProfileKind.Gaming);

        Received.InOrder(() =>
        {
            h.Config.UpdateAsync(Arg.Any<Func<AppSettings, AppSettings>>());
            h.Overlay.ApplyAsync(PowerOverlay.BestPerformance);
        });
    }

    [Fact]
    public async Task SetsMinimumNotificationLevelPerProfile()
    {
        var h = new Harness();
        var engine = h.Build();

        await engine.ApplyAsync(ProfileKind.Gaming);
        Assert.Equal(NotificationLevel.Warning, h.Notify.MinimumLevel);

        await engine.ApplyAsync(ProfileKind.Balanced);
        Assert.Equal(NotificationLevel.Info, h.Notify.MinimumLevel);
    }

    [Fact]
    public void Inspect_ReportsDrift_WhenOverlayDiffers()
    {
        var h = new Harness(ProfileKind.Work);
        h.Overlay.ReadActive().Returns(PowerOverlay.BestPerformance);
        var engine = h.Build();

        ProfileDrift? reported = null;
        engine.DriftDetected += d => reported = d;

        var drift = engine.Inspect();

        Assert.True(drift.HasDrift);
        Assert.Equal(PowerOverlay.BestEfficiency, drift.ExpectedOverlay);
        Assert.Equal(PowerOverlay.BestPerformance, drift.ActualOverlay);
        Assert.NotNull(reported);
    }

    [Fact]
    public void Inspect_ReportsNoDrift_WhenOverlayMatches()
    {
        var h = new Harness(ProfileKind.Work);
        h.Overlay.ReadActive().Returns(PowerOverlay.BestEfficiency);
        var engine = h.Build();

        bool raised = false;
        engine.DriftDetected += _ => raised = true;

        Assert.False(engine.Inspect().HasDrift);
        Assert.False(raised);
    }

    [Fact]
    public async Task UnknownProfileFallsBackToBalanced()
    {
        var h = new Harness();
        var engine = h.Build();

        await engine.ApplyAsync((ProfileKind)99);

        await h.Overlay.Received(1).ApplyAsync(PowerOverlay.Recommended);
    }
}
