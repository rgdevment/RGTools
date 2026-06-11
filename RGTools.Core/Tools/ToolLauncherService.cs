namespace RGTools.App.Core;

public sealed class ToolLauncherService : IToolLauncher
{
    private readonly IToolRunner _runner;

    public ToolLauncherService(IToolRunner runner) => _runner = runner;

    public bool Launch(ToolDescriptor tool)
    {
        if (!tool.IsCloned || !tool.IsValid) return false;

        var launch = tool.Manifest!.Launch;
        if (string.IsNullOrWhiteSpace(launch.Command)) return false;

        return _runner.Launch(launch.Command, tool.RepoPath!);
    }
}
