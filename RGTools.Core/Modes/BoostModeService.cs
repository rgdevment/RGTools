namespace RGTools.App.Core;

public sealed class BoostModeService : IMode
{
    private readonly IPowerPlanService _power;
    private readonly INotificationService _notify;

    public BoostModeService(IPowerPlanService power, INotificationService notify)
    {
        _power = power;
        _notify = notify;
    }

    public ProfileKind Kind => ProfileKind.Boost;

    public async Task ActivateAsync(CancellationToken ct = default)
    {
        await ModeRestore.TryAsync(_power.ApplyHighPerformanceAsync, "BOOST", "power").ConfigureAwait(false);
        _notify.MinimumLevel = NotificationLevel.Info;
        _notify.Notify("⚡ Modo Boost", "CPU al máximo sostenido · apps y notificaciones intactas");
    }

    public async Task DeactivateAsync(CancellationToken ct = default)
    {
        await ModeRestore.TryAsync(_power.RestoreAsync, "BOOST", "power").ConfigureAwait(false);
    }
}
