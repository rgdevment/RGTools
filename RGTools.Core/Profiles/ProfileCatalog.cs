namespace RGTools.App.Core;

public static class ProfileCatalog
{
    public static readonly ProfileDefinition Balanced = new()
    {
        Kind = ProfileKind.Balanced,
        DisplayName = "Equilibrado",
        Overlay = PowerOverlay.Recommended,
        Summary = "Estado neutro de Windows · nada modificado"
    };

    public static readonly ProfileDefinition Work = new()
    {
        Kind = ProfileKind.Work,
        DisplayName = "Trabajo",
        Overlay = PowerOverlay.BestEfficiency,
        Summary = "Máxima eficiencia · apps y notificaciones intactas"
    };

    public static readonly ProfileDefinition Gaming = new()
    {
        Kind = ProfileKind.Gaming,
        DisplayName = "Juego",
        Overlay = PowerOverlay.BestPerformance,
        GamingTweaks = true,
        SilenceNotifications = true,
        GpuPriority = true,
        Apps = AppPolicy.GamingHybrid,
        MinimumNotificationLevel = NotificationLevel.Warning,
        Summary = "Máximo rendimiento · apps de fondo en modo eficiencia · No molestar"
    };

    public static readonly IReadOnlyList<ProfileDefinition> All = new[] { Balanced, Work, Gaming };

    public static ProfileDefinition For(ProfileKind kind) =>
        All.FirstOrDefault(p => p.Kind == kind) ?? Balanced;
}
