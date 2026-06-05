namespace RGTools.App.Core;

public static class StateKeys
{
    public const string Workload = "gaming-workload";
    public const string Gpu = "gaming-gpu";
    public const string Display = "gaming-display";
    public const string Tweaks = "gaming-tweaks";
    public const string Toasts = "notification-toasts";
    public const string PowerScheme = "power-scheme";
    public const string RunMarker = "session-running";

    public static readonly string[] All = { Workload, Gpu, Display, Tweaks, Toasts, PowerScheme };
}
