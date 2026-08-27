namespace RGTools.App.Core;

public enum AppPolicy
{
    Untouched,
    GamingHybrid
}

public sealed record ProfileDefinition
{
    public required ProfileKind Kind { get; init; }
    public required string DisplayName { get; init; }
    public required PowerOverlay Overlay { get; init; }

    public bool GamingTweaks { get; init; }
    public bool SilenceNotifications { get; init; }
    public bool GpuPriority { get; init; }
    public AppPolicy Apps { get; init; } = AppPolicy.Untouched;
    public NotificationLevel MinimumNotificationLevel { get; init; } = NotificationLevel.Info;

    public required string Summary { get; init; }
}

public sealed record ProfileDrift
{
    public required ProfileKind Expected { get; init; }
    public required PowerOverlay ExpectedOverlay { get; init; }
    public required PowerOverlay ActualOverlay { get; init; }

    public bool HasDrift => ExpectedOverlay != ActualOverlay;
}
