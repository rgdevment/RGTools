namespace RGTools.App.Core;

public interface IProfileEngine
{
    ProfileKind Active { get; }

    bool IsApplying { get; }

    event Action<ProfileKind>? ProfileChanged;

    event Action<ProfileDrift>? DriftDetected;

    Task ApplyAsync(ProfileKind target, CancellationToken ct = default);

    ProfileDrift Inspect();

    Task RestoreSessionAsync(CancellationToken ct = default);

    void MarkCleanShutdown();
}
