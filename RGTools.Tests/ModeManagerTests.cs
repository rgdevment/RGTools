using NSubstitute;
using RGTools.App.Core;
using Xunit;

namespace RGTools.Tests;

public class ModeManagerTests
{
    private static IMode FakeMode(ProfileKind kind)
    {
        var mode = Substitute.For<IMode>();
        mode.Kind.Returns(kind);
        mode.ActivateAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        mode.DeactivateAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        return mode;
    }

    private static IConfigService FakeConfig(ProfileKind initial = ProfileKind.Work)
    {
        var config = Substitute.For<IConfigService>();
        config.Current.Returns(new AppSettings { ActiveProfile = initial });
        config.SaveAsync(Arg.Any<AppSettings>()).Returns(Task.CompletedTask);
        return config;
    }

    private static ISystemStateStore FakeStore(params string[] existingKeys)
    {
        var store = Substitute.For<ISystemStateStore>();
        store.Exists(Arg.Any<string>()).Returns(ci => existingKeys.Contains((string)ci[0]));
        return store;
    }

    [Fact]
    public void Active_DefaultsToConfiguredProfile()
    {
        var mgr = new ModeManager(new[] { FakeMode(ProfileKind.Work), FakeMode(ProfileKind.Zen) }, FakeConfig(ProfileKind.Zen), FakeStore());

        Assert.Equal(ProfileKind.Zen, mgr.Active);
    }

    [Fact]
    public async Task SwitchTo_ActivatesTarget_DeactivatesCurrent_Persists()
    {
        var work = FakeMode(ProfileKind.Work);
        var gaming = FakeMode(ProfileKind.Gaming);
        var config = FakeConfig(ProfileKind.Work);
        var mgr = new ModeManager(new[] { work, gaming }, config, FakeStore());

        await mgr.SwitchToAsync(ProfileKind.Gaming);

        Assert.Equal(ProfileKind.Gaming, mgr.Active);
        await work.Received(1).DeactivateAsync(Arg.Any<CancellationToken>());
        await gaming.Received(1).ActivateAsync(Arg.Any<CancellationToken>());
        await config.Received(1).SaveAsync(Arg.Is<AppSettings>(s => s.ActiveProfile == ProfileKind.Gaming));
    }

    [Fact]
    public async Task SwitchTo_PersistsIntent_BeforeActivating()
    {
        var work = FakeMode(ProfileKind.Work);
        var gaming = FakeMode(ProfileKind.Gaming);
        var config = FakeConfig(ProfileKind.Work);
        var mgr = new ModeManager(new[] { work, gaming }, config, FakeStore());

        await mgr.SwitchToAsync(ProfileKind.Gaming);

        Received.InOrder(() =>
        {
            config.SaveAsync(Arg.Is<AppSettings>(s => s.ActiveProfile == ProfileKind.Gaming));
            gaming.ActivateAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task SwitchTo_SameMode_IsNoOp()
    {
        var work = FakeMode(ProfileKind.Work);
        var mgr = new ModeManager(new[] { work }, FakeConfig(ProfileKind.Work), FakeStore());

        await mgr.SwitchToAsync(ProfileKind.Work);

        await work.DidNotReceive().ActivateAsync(Arg.Any<CancellationToken>());
        await work.DidNotReceive().DeactivateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SwitchTo_RaisesModeChanged()
    {
        var mgr = new ModeManager(new[] { FakeMode(ProfileKind.Work), FakeMode(ProfileKind.Gaming) }, FakeConfig(), FakeStore());
        ProfileKind? raised = null;
        mgr.ModeChanged += k => raised = k;

        await mgr.SwitchToAsync(ProfileKind.Gaming);

        Assert.Equal(ProfileKind.Gaming, raised);
    }

    [Fact]
    public async Task SwitchTo_UnknownMode_IsIgnored()
    {
        var work = FakeMode(ProfileKind.Work);
        var mgr = new ModeManager(new[] { work }, FakeConfig(ProfileKind.Work), FakeStore());

        await mgr.SwitchToAsync(ProfileKind.Gaming);

        Assert.Equal(ProfileKind.Work, mgr.Active);
        await work.DidNotReceive().DeactivateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SwitchTo_WhenActivateFails_ForcesWork()
    {
        var work = FakeMode(ProfileKind.Work);
        var gaming = FakeMode(ProfileKind.Gaming);
        var zen = FakeMode(ProfileKind.Zen);
        zen.ActivateAsync(Arg.Any<CancellationToken>())
           .Returns(Task.FromException(new InvalidOperationException("boom")));
        var mgr = new ModeManager(new[] { work, gaming, zen }, FakeConfig(ProfileKind.Gaming), FakeStore());

        await mgr.SwitchToAsync(ProfileKind.Zen);

        Assert.Equal(ProfileKind.Work, mgr.Active);
        await work.Received(1).ActivateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void IsDirty_TrueWhenOrphanSnapshotExists()
    {
        var mgr = new ModeManager(new[] { FakeMode(ProfileKind.Work) }, FakeConfig(), FakeStore(StateKeys.Gpu));

        Assert.True(mgr.IsDirty);
    }

    [Fact]
    public void IsDirty_FalseWhenNoSnapshots()
    {
        var mgr = new ModeManager(new[] { FakeMode(ProfileKind.Work) }, FakeConfig(), FakeStore());

        Assert.False(mgr.IsDirty);
    }

    [Fact]
    public async Task SanitizeToWork_ActivatesWork()
    {
        var work = FakeMode(ProfileKind.Work);
        var mgr = new ModeManager(new[] { work, FakeMode(ProfileKind.Gaming) }, FakeConfig(ProfileKind.Gaming), FakeStore(StateKeys.Gpu));

        await mgr.SanitizeToWorkAsync();

        Assert.Equal(ProfileKind.Work, mgr.Active);
        await work.Received(1).ActivateAsync(Arg.Any<CancellationToken>());
    }
}
