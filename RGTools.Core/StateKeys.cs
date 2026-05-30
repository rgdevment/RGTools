namespace RGTools.App.Core;

public static class StateKeys
{
    public const string Workload = "gaming-workload";
    public const string Gpu = "gaming-gpu";
    public const string Toasts = "notification-toasts";
    public const string PowerScheme = "power-scheme";
    public const string ZenHosts = "zen-hosts";

    public static readonly string[] All = { Workload, Gpu, Toasts, PowerScheme, ZenHosts };
}
