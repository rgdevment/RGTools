using System.IO;
using NSubstitute;
using RGTools.App.Core;
using Xunit;

namespace RGTools.Tests;

public class ToolPilotTests
{
    private const string ValidJson = """
    {
      "schema": 1,
      "id": "videomerge",
      "name": "videomerge",
      "description": "x",
      "category": "Productivity",
      "requirements": { "runtime": "python>=3.10", "system": ["ffmpeg>=4"] },
      "provision": { "strategy": "ManagedEnv", "command": "uv sync" },
      "preflight": "uv run vm tools --check",
      "launch": { "kind": "Interpreter", "command": "uv run vm" },
      "version": "uv run vm --version",
      "elevated": false
    }
    """;

    [Fact]
    public void Parse_ValidManifest_MapsFields()
    {
        var m = ToolRegistryService.Parse(ValidJson);

        Assert.NotNull(m);
        Assert.Equal("videomerge", m!.Id);
        Assert.Equal(ToolCategory.Productivity, m.Category);
        Assert.Equal(ProvisionStrategy.ManagedEnv, m.Provision.Strategy);
        Assert.Equal("uv sync", m.Provision.Command);
        Assert.Equal(LaunchKind.Interpreter, m.Launch.Kind);
        Assert.Equal("uv run vm", m.Launch.Command);
    }

    [Fact]
    public void Parse_UnsupportedSchema_ReturnsNull()
    {
        Assert.Null(ToolRegistryService.Parse("""{ "schema": 99, "id": "x" }"""));
    }

    private static ToolDescriptor Tool(string? repo, ToolManifest? manifest) =>
        new() { Id = "videomerge", RepoPath = repo, Manifest = manifest };

    private static ToolManifest Manifest(string preflight = "") =>
        new()
        {
            Schema = 1,
            Id = "videomerge",
            Provision = new ToolProvision { Strategy = ProvisionStrategy.ManagedEnv, Command = "uv sync" },
            Preflight = preflight,
            Launch = new ToolLaunchSpec { Kind = LaunchKind.Interpreter, Command = "uv run vm" }
        };

    [Fact]
    public async Task Detect_NotCloned_WhenNoRepoPath()
    {
        var p = new ToolProvisionerService(Substitute.For<IToolRunner>());
        Assert.Equal(ProvisionState.NotCloned, await p.DetectAsync(Tool(null, null)));
    }

    [Fact]
    public async Task Detect_Broken_WhenManifestMissing()
    {
        var p = new ToolProvisionerService(Substitute.For<IToolRunner>());
        Assert.Equal(ProvisionState.Broken, await p.DetectAsync(Tool(@"C:\repo", null)));
    }

    [Fact]
    public async Task Detect_Ready_WhenPreflightSucceeds()
    {
        var runner = Substitute.For<IToolRunner>();
        runner.RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new ToolRunResult(0, ""));

        var p = new ToolProvisionerService(runner);
        Assert.Equal(ProvisionState.Ready, await p.DetectAsync(Tool(@"C:\repo", Manifest(preflight: "uv run vm tools --check"))));
    }

    [Fact]
    public async Task Detect_NotReady_WhenPreflightFails()
    {
        var runner = Substitute.For<IToolRunner>();
        runner.RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new ToolRunResult(1, "boom"));

        var p = new ToolProvisionerService(runner);
        Assert.Equal(ProvisionState.NotReady, await p.DetectAsync(Tool(@"C:\repo", Manifest(preflight: "uv run vm tools --check"))));
    }

    [Fact]
    public async Task Ensure_RunsProvisionCommandInRepo()
    {
        var runner = Substitute.For<IToolRunner>();
        runner.RunAsync("uv sync", @"C:\repo", Arg.Any<CancellationToken>()).Returns(new ToolRunResult(0, ""));

        var p = new ToolProvisionerService(runner);

        var result = await p.EnsureAsync(Tool(@"C:\repo", Manifest()));

        Assert.True(result.Success);
        await runner.Received(1).RunAsync("uv sync", @"C:\repo", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ensure_ReturnsCommandOutputOnFailure()
    {
        var runner = Substitute.For<IToolRunner>();
        runner.RunAsync("uv sync", @"C:\repo", Arg.Any<CancellationToken>())
              .Returns(new ToolRunResult(9009, "'uv' no se reconoce como un comando"));

        var p = new ToolProvisionerService(runner);

        var result = await p.EnsureAsync(Tool(@"C:\repo", Manifest()));

        Assert.False(result.Success);
        Assert.Equal(9009, result.ExitCode);
        Assert.Contains("uv", result.Output);
    }

    [Fact]
    public void Launch_UsesManifestCommandAndRepo()
    {
        var runner = Substitute.For<IToolRunner>();
        runner.Launch("uv run vm", @"C:\repo").Returns(true);

        var l = new ToolLauncherService(runner);

        Assert.True(l.Launch(Tool(@"C:\repo", Manifest())));
        runner.Received(1).Launch("uv run vm", @"C:\repo");
    }

    [Fact]
    public async Task Acquire_NoUrlOrTarget_ReturnsFailure()
    {
        var p = new ToolProvisionerService(Substitute.For<IToolRunner>());

        var result = await p.AcquireAsync(new ToolDescriptor { Id = "videomerge" });

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Acquire_RunsGitClone_WhenTargetMissing()
    {
        var runner = Substitute.For<IToolRunner>();
        runner.RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
              .Returns(new ToolRunResult(0, ""));

        string target = Path.Combine(Path.GetTempPath(), "rgtools_clone_test", "videomerge");
        try
        {
            var p = new ToolProvisionerService(runner);
            var tool = new ToolDescriptor
            {
                Id = "videomerge",
                RepoUrl = "git@github.com:rgdevment/videomerge.git",
                CloneTarget = target
            };

            var result = await p.AcquireAsync(tool);

            Assert.True(result.Success);
            await runner.Received(1).RunAsync(
                Arg.Is<string>(c => c.Contains("git clone") && c.Contains("videomerge.git")),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            string parent = Path.Combine(Path.GetTempPath(), "rgtools_clone_test");
            if (Directory.Exists(parent)) Directory.Delete(parent, true);
        }
    }

    [Fact]
    public async Task Acquire_TargetExists_SkipsClone()
    {
        var runner = Substitute.For<IToolRunner>();
        string target = Path.Combine(Path.GetTempPath(), "rgtools_clone_existing");
        Directory.CreateDirectory(target);
        try
        {
            var p = new ToolProvisionerService(runner);
            var tool = new ToolDescriptor { Id = "videomerge", RepoUrl = "x", CloneTarget = target };

            var result = await p.AcquireAsync(tool);

            Assert.True(result.Success);
            await runner.DidNotReceive().RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            if (Directory.Exists(target)) Directory.Delete(target, true);
        }
    }
}
