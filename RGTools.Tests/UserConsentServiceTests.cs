using NSubstitute;
using RGTools.App.Core;
using Xunit;

namespace RGTools.Tests;

public class UserConsentServiceTests
{
    private static IConfigService ConfigWith(params string[] grantedOps)
    {
        var config = Substitute.For<IConfigService>();
        var granted = grantedOps.ToDictionary(op => op, _ => true);
        config.Current.Returns(new AppSettings { Consent = new ConsentSettings { Granted = granted } });
        config.SaveAsync(Arg.Any<AppSettings>()).Returns(Task.CompletedTask);
        config.UpdateAsync(Arg.Any<Func<AppSettings, AppSettings>>()).Returns(Task.CompletedTask);
        return config;
    }

    [Fact]
    public void IsGranted_ReturnsTrue_ForStoredOperation()
    {
        var svc = new UserConsentService(ConfigWith("gaming.gpu-priority"));

        Assert.True(svc.IsGranted("gaming.gpu-priority"));
    }

    [Fact]
    public void IsGranted_ReturnsFalse_ForUnknownOperation()
    {
        var svc = new UserConsentService(ConfigWith());

        Assert.False(svc.IsGranted("gaming.unknown-op"));
    }

    [Fact]
    public async Task RequestAsync_ReturnsTrue_WithoutResaving_WhenAlreadyGranted()
    {
        var config = ConfigWith("op1");
        var svc = new UserConsentService(config);

        bool result = await svc.RequestAsync("op1", "title", "detail");

        Assert.True(result);
        await config.DidNotReceive().UpdateAsync(Arg.Any<Func<AppSettings, AppSettings>>());
    }
}
