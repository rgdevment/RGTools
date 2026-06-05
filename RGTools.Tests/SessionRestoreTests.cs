using NSubstitute;
using RGTools.App.Core;
using Xunit;

namespace RGTools.Tests;

public class SessionRestoreTests
{
    private static IMode FakeMode(ProfileKind kind)
    {
        var mode = Substitute.For<IMode>();
        mode.Kind.Returns(kind);
        mode.ActivateAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        mode.DeactivateAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        return mode;
    }

    private static IConfigService FakeConfig(ProfileKind initial)
    {
        var config = Substitute.For<IConfigService>();
        config.Current.Returns(new AppSettings { ActiveProfile = initial });
        config.SaveAsync(Arg.Any<AppSettings>()).Returns(Task.CompletedTask);
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
    public async Task RestoreSession_CleanShutdown_KeepsGamingProfile()
    {
        var work = FakeMode(ProfileKind.Work);
        var gaming = FakeMode(ProfileKind.Gaming);
        // No RunMarker => previous session exited cleanly.
        var mgr = new ModeManager(new[] { work, gaming }, FakeConfig(ProfileKind.Gaming), FakeStore());

        await mgr.RestoreSessionAsync();

        Assert.Equal(ProfileKind.Gaming, mgr.Active);
        await work.DidNotReceive().ActivateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RestoreSession_AfterCrash_SanitizesToWork()
    {
        var work = FakeMode(ProfileKind.Work);
        var gaming = FakeMode(ProfileKind.Gaming);
        // RunMarker present => previous session crashed.
        var mgr = new ModeManager(new[] { work, gaming }, FakeConfig(ProfileKind.Gaming), FakeStore(StateKeys.RunMarker));

        await mgr.RestoreSessionAsync();

        Assert.Equal(ProfileKind.Work, mgr.Active);
        await work.Received(1).ActivateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RestoreSession_WritesRunMarker()
    {
        var store = FakeStore();
        var mgr = new ModeManager(new[] { FakeMode(ProfileKind.Work) }, FakeConfig(ProfileKind.Work), store);

        await mgr.RestoreSessionAsync();

        await store.Received().SaveAsync(StateKeys.RunMarker, Arg.Any<object>());
    }

    [Fact]
    public void MarkCleanShutdown_ClearsRunMarker()
    {
        var store = FakeStore(StateKeys.RunMarker);
        var mgr = new ModeManager(new[] { FakeMode(ProfileKind.Work) }, FakeConfig(ProfileKind.Work), store);

        mgr.MarkCleanShutdown();

        store.Received(1).Clear(StateKeys.RunMarker);
    }
}
