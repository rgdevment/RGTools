using NSubstitute;
using RGTools.App.Core;
using Xunit;

namespace RGTools.Tests;

public class WorkloadGuardTests
{
    private static readonly string[] Throttled =
    {
        "Slack", "Discord", "WhatsApp", "Spark Desktop", "SearchIndexer"
    };

    private static (WorkloadGuardService guard, IProcessRunner runner, IProcessThrottler throttler) Build(string capture = "")
    {
        var runner = Substitute.For<IProcessRunner>();
        runner.RunPowerShellCaptureAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(capture);
        var throttler = Substitute.For<IProcessThrottler>();
        return (new WorkloadGuardService(runner, throttler), runner, throttler);
    }

    [Fact]
    public async Task Capture_ReadsTheDockerServiceState()
    {
        var (guard, _, _) = Build(capture: "Running");

        Assert.True((await guard.CaptureAsync()).DockerServiceWasRunning);
    }

    [Fact]
    public async Task Capture_ReportsStoppedWhenServiceIsAbsent()
    {
        var (guard, _, _) = Build(capture: "");

        Assert.False((await guard.CaptureAsync()).DockerServiceWasRunning);
    }

    [Fact]
    public async Task Suspend_ThrottlesTheChatAppsInsteadOfClosingThem()
    {
        var (guard, runner, throttler) = Build();

        await guard.SuspendAsync();

        foreach (var name in Throttled)
            throttler.Received(1).SetEfficiency(name, true);

        await runner.Received(1).RunPowerShellAsync(
            Arg.Is<string>(s => !s.Contains("'Slack") && !s.Contains("'Discord")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Suspend_ClosesTheHeavyAppsAndShutsDownWsl()
    {
        var (guard, runner, _) = Build();

        await guard.SuspendAsync();

        await runner.Received(1).RunPowerShellAsync(
            Arg.Is<string>(s =>
                s.Contains("'Docker Desktop*'")
                && s.Contains("'LM Studio*'")
                && s.Contains("'qbittorrent*'")
                && s.Contains("wsl --shutdown")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Suspend_LeavesWindowsSearchServiceRunning()
    {
        var (guard, runner, _) = Build();

        await guard.SuspendAsync();

        await runner.DidNotReceive().RunPowerShellAsync(
            Arg.Is<string>(s => s.Contains("Stop-Service -Name 'WSearch'")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Restore_LiftsEfficiencyModeAndStartsDockerWhenItWasRunning()
    {
        var (guard, runner, throttler) = Build();

        await guard.RestoreAsync(new WorkloadSnapshot { DockerServiceWasRunning = true });

        foreach (var name in Throttled)
            throttler.Received(1).SetEfficiency(name, false);

        await runner.Received(1).RunPowerShellAsync(
            Arg.Is<string>(s => s.Contains("Start-Service")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Restore_WithoutSnapshot_StillLiftsEfficiencyMode()
    {
        var (guard, runner, throttler) = Build();

        await guard.RestoreAsync(null);

        throttler.Received(1).SetEfficiency("Slack", false);
        await runner.DidNotReceive().RunPowerShellAsync(
            Arg.Is<string>(s => s.Contains("Start-Service")), Arg.Any<CancellationToken>());
    }
}
