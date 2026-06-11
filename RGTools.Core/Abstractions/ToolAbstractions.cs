namespace RGTools.App.Core;

public interface IToolRunner
{
    // Runs a shell command line in workingDirectory, waits, returns exit code + combined output.
    Task<ToolRunResult> RunAsync(string commandLine, string workingDirectory, CancellationToken ct = default);

    // Launches a command line in its own visible console (for interactive CLI/TUI tools). Does not wait.
    bool Launch(string commandLine, string workingDirectory);
}

public interface IToolRegistry
{
    IReadOnlyList<ToolDescriptor> All { get; }
    ToolDescriptor? Find(string id);
    Task ReloadAsync(CancellationToken ct = default);
}

public interface IToolProvisioner
{
    Task<ProvisionState> DetectAsync(ToolDescriptor tool, CancellationToken ct = default);
    Task<ToolRunResult> EnsureAsync(ToolDescriptor tool, CancellationToken ct = default);
}

public interface IToolLauncher
{
    bool Launch(ToolDescriptor tool);
}
