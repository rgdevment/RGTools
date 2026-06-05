using NSubstitute;
using RGTools.App.Core;
using Xunit;

namespace RGTools.Tests;

public class StartupServiceTests
{
    [Fact]
    public async Task SetStartup_Enable_WrapsExecutablePathInDoubleQuotes()
    {
        string? captured = null;
        var runner = Substitute.For<IProcessRunner>();
        runner.RunAsync(Arg.Any<string>(), Arg.Do<string>(a => captured = a), Arg.Any<CancellationToken>()).Returns(0);
        var svc = new StartupService(runner);

        await svc.SetStartupAsync(true);

        Assert.NotNull(captured);
        Assert.Contains("/tr \"\\\"", captured);
        Assert.DoesNotContain("/tr \"'", captured);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(-1, null)]
    public async Task IsEnabled_MapsExitCodeToTriState(int exit, bool? expected)
    {
        var runner = Substitute.For<IProcessRunner>();
        runner.RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(exit);
        var svc = new StartupService(runner);

        var result = await svc.IsEnabledAsync();

        Assert.Equal(expected, result);
    }
}
