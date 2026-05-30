namespace RGTools.App.Core;

public interface IModeManager
{
    ProfileKind Active { get; }

    bool IsTransitioning { get; }

    event Action<ProfileKind>? ModeChanged;

    Task SwitchToAsync(ProfileKind target, CancellationToken ct = default);
}
