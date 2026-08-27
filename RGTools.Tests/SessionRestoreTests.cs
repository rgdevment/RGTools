using NSubstitute;
using RGTools.App.Core;
using Xunit;

namespace RGTools.Tests;

public class SessionRestoreTests
{
    private static ProfileEngine Build(
        ProfileKind stored,
        IPowerOverlayService overlay,
        ISystemStateStore store,
        IConfigService? config = null)
    {
        config ??= FakeConfig(stored);

        return new ProfileEngine(
            overlay,
            Substitute.For<IWorkloadGuard>(),
            Substitute.For<IGamingTweaksService>(),
            Substitute.For<INotificationSilencer>(),
            Substitute.For<IGpuPriorityService>(),
            Substitute.For<IUserConsentService>(),
            config,
            store,
            Substitute.For<INotificationService>());
    }

    private static IConfigService FakeConfig(ProfileKind initial)
    {
        var config = Substitute.For<IConfigService>();
        config.Current.Returns(new AppSettings { ActiveProfile = initial });
        config.UpdateAsync(Arg.Any<Func<AppSettings, AppSettings>>()).Returns(Task.CompletedTask);
        return config;
    }

    private static ISystemStateStore FakeStore(params string[] existingKeys)
    {
        var store = Substitute.For<ISystemStateStore>();
        var keys = new HashSet<string>(existingKeys);
        store.Exists(Arg.Any<string>()).Returns(ci => keys.Contains((string)ci[0]));
        store.SaveAsync(Arg.Any<string>(), Arg.Any<object>()).Returns(Task.CompletedTask);
        return store;
    }

    [Fact]
    public async Task CleanShutdown_ReappliesTheStoredProfile()
    {
        var overlay = Substitute.For<IPowerOverlayService>();
        var engine = Build(ProfileKind.Gaming, overlay, FakeStore());

        await engine.RestoreSessionAsync();

        Assert.Equal(ProfileKind.Gaming, engine.Active);
        await overlay.Received(1).ApplyAsync(PowerOverlay.BestPerformance);
    }

    [Fact]
    public async Task AfterCrash_ResetsToBalanced()
    {
        var overlay = Substitute.For<IPowerOverlayService>();
        var engine = Build(ProfileKind.Gaming, overlay, FakeStore(StateKeys.RunMarker));

        await engine.RestoreSessionAsync();

        Assert.Equal(ProfileKind.Balanced, engine.Active);
        await overlay.Received(1).ApplyAsync(PowerOverlay.Recommended);
        await overlay.DidNotReceive().ApplyAsync(PowerOverlay.BestPerformance);
    }

    [Fact]
    public async Task WritesRunMarkerBeforeApplying()
    {
        var store = FakeStore();
        var overlay = Substitute.For<IPowerOverlayService>();
        var engine = Build(ProfileKind.Work, overlay, store);

        await engine.RestoreSessionAsync();

        Received.InOrder(() =>
        {
            store.SaveAsync(StateKeys.RunMarker, Arg.Any<object>());
            overlay.ApplyAsync(PowerOverlay.BestEfficiency);
        });
    }

    [Fact]
    public void MarkCleanShutdown_ClearsRunMarker()
    {
        var store = FakeStore(StateKeys.RunMarker);
        var engine = Build(ProfileKind.Work, Substitute.For<IPowerOverlayService>(), store);

        engine.MarkCleanShutdown();

        store.Received(1).Clear(StateKeys.RunMarker);
    }
}
