namespace RGTools.App.Core;

public enum ProfileKind
{
    Work,
    Gaming
}

public record AppSettings
{
    public bool DnsGuardianEnabled { get; init; } = true;
    public bool StartWithWindows { get; init; } = false;
    public ProfileKind ActiveProfile { get; init; } = ProfileKind.Work;
    public ConsentSettings Consent { get; init; } = new();

    public string? JumboxFolderPath { get; init; }
}

public record ConsentSettings
{
    public Dictionary<string, bool> Granted { get; init; } = new();
}
