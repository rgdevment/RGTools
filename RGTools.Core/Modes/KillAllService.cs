namespace RGTools.App.Core;

public sealed class KillAllService : IKillAllService
{
    private readonly IModeManager _modeManager;
    private readonly IVpnService _vpn;
    private readonly INotificationService _notify;

    public KillAllService(
        IModeManager modeManager,
        IVpnService vpn,
        INotificationService notify)
    {
        _modeManager = modeManager;
        _vpn = vpn;
        _notify = notify;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        LogService.Log("[RESET] Clean-state sequence initiated.");

        try
        {
            await _modeManager.SwitchToAsync(ProfileKind.Work, ct);

            if (_vpn.IsActive)
                await _vpn.ToggleAsync();

            _notify.Notify("🧹 RGTools", "Estado limpio · perfil Trabajo · DNS sigue activo", NotificationLevel.Critical);
            LogService.Log("[RESET] Completed. App stays running, DNS Guardian untouched.");
        }
        catch (Exception ex)
        {
            LogService.LogCrash("[RESET] Sequence error", ex);
        }
    }
}
