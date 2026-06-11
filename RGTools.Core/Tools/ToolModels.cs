namespace RGTools.App.Core;

public enum ToolCategory { Network, Privacy, Productivity, System, Database }

public enum ProvisionStrategy { None, ManagedEnv, ScriptInstaller, PrebuiltBinary, SystemPackage }

public enum LaunchKind { Exe, Interpreter }

public enum ProvisionState { NotCloned, NotReady, Ready, Broken }

public sealed record ToolRunResult(int ExitCode, string Output)
{
    public bool Success => ExitCode == 0;
}

public sealed record ToolRequirements
{
    public string Runtime { get; init; } = "";
    public string[] System { get; init; } = Array.Empty<string>();
}

public sealed record ToolProvision
{
    public ProvisionStrategy Strategy { get; init; }
    public string Command { get; init; } = "";
}

public sealed record ToolLaunchSpec
{
    public LaunchKind Kind { get; init; }
    public string Command { get; init; } = "";
}

public sealed record ToolManifest
{
    public int Schema { get; init; }
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public ToolCategory Category { get; init; }
    public ToolRequirements Requirements { get; init; } = new();
    public ToolProvision Provision { get; init; } = new();
    public string Preflight { get; init; } = "";
    public ToolLaunchSpec Launch { get; init; } = new();
    public string Version { get; init; } = "";
    public bool Elevated { get; init; }
}

public sealed record ToolIndexEntry(string Id, string Folder);

public sealed record ToolDescriptor
{
    public required string Id { get; init; }
    public string? RepoPath { get; init; }
    public ToolManifest? Manifest { get; init; }

    public bool IsCloned => RepoPath != null;
    public bool IsValid => Manifest is { Schema: 1 };
    public string DisplayName => Manifest?.Name ?? Id;
}
