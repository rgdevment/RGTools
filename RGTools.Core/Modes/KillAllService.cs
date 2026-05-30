using System.Windows;

namespace RGTools.App.Core;

public sealed class KillAllService : IKillAllService
{
    private readonly IModeManager _modeManager;
    private readonly IVpnService _vpn;
    private readonly IDnsGuardianService _dns;
    private readonly INotificationService _notify;

    public KillAllService(
        IModeManager modeManager,
        IVpnService vpn,
        IDnsGuardianService dns,
        INotificationService notify)
    {
        _modeManager = modeManager;
        _vpn = vpn;
        _dns = dns;
        _notify = notify;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        LogService.Log("[KILLALL] Sequence initiated.");

        try
        {
            await _modeManager.SwitchToAsync(ProfileKind.Work, ct);

            if (_vpn.IsActive)
                await _vpn.ToggleAsync();

            _dns.Stop();

            _notify.Notify("🔴 RGTools", "Apagado limpio completado", NotificationLevel.Critical);
            LogService.Log("[KILLALL] Completed. Shutting down.");
        }
        catch (Exception ex)
        {
            LogService.LogCrash("[KILLALL] Sequence error", ex);
        }
        finally
        {
            Application.Current?.Dispatcher.Invoke(() => Application.Current.Shutdown());
        }
    }
}
