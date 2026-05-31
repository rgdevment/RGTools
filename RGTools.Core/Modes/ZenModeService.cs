namespace RGTools.App.Core;

public sealed class ZenModeService : IMode, IDisposable
{
    private const string HostsConsentId = "zen.hosts-block";

    private readonly IConfigService _config;
    private readonly IUserConsentService _consent;
    private readonly INotificationService _notify;
    private readonly IHostsBlocker _hosts;

    private CancellationTokenSource? _pomodoroCts;

    public ZenModeService(
        IConfigService config,
        IUserConsentService consent,
        INotificationService notify,
        IHostsBlocker hosts)
    {
        _config = config;
        _consent = consent;
        _notify = notify;
        _hosts = hosts;
    }

    public ProfileKind Kind => ProfileKind.Zen;

    public async Task ActivateAsync(CancellationToken ct = default)
    {
        _notify.MinimumLevel = NotificationLevel.Critical;

        var blocked = _config.Current.ZenBlockedHosts;
        if (blocked.Count > 0 && await _consent.RequestAsync(HostsConsentId,
                "Modo Zen — Bloqueo de sitios",
                $"¿Bloquear {blocked.Count} sitio(s) editando el archivo hosts? Se revierte al salir."))
        {
            await _hosts.ApplyAsync(blocked);
        }

        StartPomodoro();

        _notify.Notify("🧘 Modo Zen", "Notificaciones silenciadas · Pomodoro iniciado", NotificationLevel.Critical);
    }

    public async Task DeactivateAsync(CancellationToken ct = default)
    {
        StopPomodoro();
        await _hosts.RestoreAsync();
        _notify.MinimumLevel = NotificationLevel.Info;
    }

    private void StartPomodoro()
    {
        _pomodoroCts = new CancellationTokenSource();
        _ = RunPomodoroAsync(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5), _pomodoroCts.Token);
    }

    private void StopPomodoro()
    {
        _pomodoroCts?.Cancel();
        _pomodoroCts?.Dispose();
        _pomodoroCts = null;
    }

    private async Task RunPomodoroAsync(TimeSpan focus, TimeSpan rest, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                _notify.Notify("🧘 Pomodoro", $"Enfoque: {focus.TotalMinutes:0} min", NotificationLevel.Critical);
                await Task.Delay(focus, token);

                _notify.Notify("🧘 Pomodoro", $"Descanso: {rest.TotalMinutes:0} min", NotificationLevel.Critical);
                await Task.Delay(rest, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            LogService.Log("[ZEN] Pomodoro loop crashed", ex);
        }
    }

    public void Dispose() => StopPomodoro();
}
