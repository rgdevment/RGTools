using System.IO;

namespace RGTools.App.Core;

public sealed class ZenModeService : IMode
{
    private const string HostsStateKey = StateKeys.ZenHosts;
    private const string HostsConsentId = "zen.hosts-block";
    private const string HostsMarker = "# RGTools-Zen";
    private static readonly string HostsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");

    private readonly IConfigService _config;
    private readonly IUserConsentService _consent;
    private readonly INotificationService _notify;
    private readonly ISystemStateStore _store;

    private CancellationTokenSource? _pomodoroCts;

    public ZenModeService(
        IConfigService config,
        IUserConsentService consent,
        INotificationService notify,
        ISystemStateStore store)
    {
        _config = config;
        _consent = consent;
        _notify = notify;
        _store = store;
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
            await BlockHostsAsync(blocked);
        }

        StartPomodoro();

        _notify.Notify("🧘 Modo Zen", "Notificaciones silenciadas · Pomodoro iniciado", NotificationLevel.Critical);
    }

    public async Task DeactivateAsync(CancellationToken ct = default)
    {
        StopPomodoro();
        await RestoreHostsAsync();
        _notify.MinimumLevel = NotificationLevel.Info;
    }

    private async Task BlockHostsAsync(IReadOnlyList<string> hosts)
    {
        try
        {
            await StripMarkedLinesAsync();

            var lines = hosts.Select(h => $"127.0.0.1 {h} {HostsMarker}");
            await File.AppendAllTextAsync(HostsPath, Environment.NewLine + string.Join(Environment.NewLine, lines) + Environment.NewLine);
            await _store.SaveAsync(HostsStateKey, true);
            LogService.Log($"[ZEN] Blocked {hosts.Count} host(s).");
        }
        catch (Exception ex)
        {
            LogService.Log("[ZEN] Hosts block failed", ex);
        }
    }

    private async Task RestoreHostsAsync()
    {
        if (!_store.Exists(HostsStateKey)) return;

        try
        {
            await StripMarkedLinesAsync();
            _store.Clear(HostsStateKey);
            LogService.Log("[ZEN] Hosts restored (marked lines removed).");
        }
        catch (Exception ex)
        {
            LogService.Log("[ZEN] Hosts restore failed", ex);
        }
    }

    private static async Task StripMarkedLinesAsync()
    {
        if (!File.Exists(HostsPath)) return;

        var lines = await File.ReadAllLinesAsync(HostsPath);
        var kept = lines.Where(l => !l.Contains(HostsMarker, StringComparison.OrdinalIgnoreCase)).ToArray();

        if (kept.Length != lines.Length)
            await File.WriteAllLinesAsync(HostsPath, kept);
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
}
