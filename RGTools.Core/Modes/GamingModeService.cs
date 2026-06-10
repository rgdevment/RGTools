namespace RGTools.App.Core;

public sealed class GamingModeService : IMode
{
    private const string GpuConsentId = "gaming.gpu-priority";

    private readonly IWorkloadGuard _workload;
    private readonly IPowerPlanService _power;
    private readonly IGpuPriorityService _gpu;
    private readonly IDisplayRefreshService _display;
    private readonly IGamingTweaksService _tweaks;
    private readonly INotificationSilencer _silencer;
    private readonly ISystemStateStore _store;
    private readonly IUserConsentService _consent;
    private readonly INotificationService _notify;

    public GamingModeService(
        IWorkloadGuard workload,
        IPowerPlanService power,
        IGpuPriorityService gpu,
        IDisplayRefreshService display,
        IGamingTweaksService tweaks,
        INotificationSilencer silencer,
        ISystemStateStore store,
        IUserConsentService consent,
        INotificationService notify)
    {
        _workload = workload;
        _power = power;
        _gpu = gpu;
        _display = display;
        _tweaks = tweaks;
        _silencer = silencer;
        _store = store;
        _consent = consent;
        _notify = notify;
    }

    public ProfileKind Kind => ProfileKind.Gaming;

    public async Task ActivateAsync(CancellationToken ct = default)
    {
        if (!_store.Exists(StateKeys.Workload))
        {
            var snapshot = await _workload.CaptureAsync(ct).ConfigureAwait(false);
            await _store.SaveAsync(StateKeys.Workload, snapshot).ConfigureAwait(false);
            await _workload.SuspendAsync(ct).ConfigureAwait(false);
        }

        await _power.ApplyHighPerformanceAsync().ConfigureAwait(false);
        await _silencer.SilenceAsync().ConfigureAwait(false);
        await _display.ApplyMaxAsync().ConfigureAwait(false);
        await _tweaks.ApplyAsync().ConfigureAwait(false);

        string gpuStatus;
        if (await _consent.RequestAsync(GpuConsentId,
                "Modo Gaming — GPU Priority",
                "¿Aplicar prioridad de GPU en el registro de Windows? Se revierte al salir del modo.").ConfigureAwait(false))
        {
            await _gpu.ApplyAsync().ConfigureAwait(false);
            gpuStatus = "GPU Priority on";
        }
        else
        {
            gpuStatus = "GPU Priority (sin permiso)";
        }

        _notify.Notify("🎮 Modo Gaming",
            $"Apps + Docker/WSL cerrados · Notificaciones en silencio · Máximo rendimiento · Refresh al máximo · Red optimizada · {gpuStatus}");
    }

    public async Task DeactivateAsync(CancellationToken ct = default)
    {
        await _gpu.RestoreAsync().ConfigureAwait(false);
        await _display.RestoreAsync().ConfigureAwait(false);
        await _tweaks.RestoreAsync().ConfigureAwait(false);
        await _silencer.RestoreAsync().ConfigureAwait(false);
        await _power.RestoreAsync().ConfigureAwait(false);
    }
}
