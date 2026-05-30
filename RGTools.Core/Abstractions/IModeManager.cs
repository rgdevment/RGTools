namespace RGTools.App.Core;

public interface IModeManager
{
    ProfileKind Active { get; }

    bool IsTransitioning { get; }

    bool IsDirty { get; }

    event Action<ProfileKind>? ModeChanged;

    Task SwitchToAsync(ProfileKind target, CancellationToken ct = default);

    Task SanitizeToWorkAsync(CancellationToken ct = default);
}
