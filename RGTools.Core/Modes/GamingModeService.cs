namespace RGTools.App.Core;

public sealed class GamingModeService : IMode
{
    private const string GpuConsentId = "gaming.gpu-priority";

    private readonly IWorkloadGuard _workload;
    private readonly IPowerPlanService _power;
    private readonly IGpuPriorityService _gpu;
    private readonly INotificationSilencer _silencer;
    private readonly ISystemStateStore _store;
    private readonly IUserConsentService _consent;
    private readonly INotificationService _notify;

    public GamingModeService(
        IWorkloadGuard workload,
        IPowerPlanService power,
        IGpuPriorityService gpu,
        INotificationSilencer silencer,
        ISystemStateStore store,
        IUserConsentService consent,
        INotificationService notify)
    {
        _workload = workload;
        _power = power;
        _gpu = gpu;
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
            var snapshot = await _workload.SuspendAsync(ct);
            await _store.SaveAsync(StateKeys.Workload, snapshot);
        }

        await _power.ApplyHighPerformanceAsync();
        await _silencer.SilenceAsync();

        var pending = new List<string>();

        if (await _consent.RequestAsync(GpuConsentId,
                "Modo Gaming — GPU Priority",
                "¿Aplicar prioridad de GPU en el registro de Windows? Se revierte al salir del modo."))
        {
            await _gpu.ApplyAsync();
        }
        else
        {
            pending.Add("GPU Priority (sin permiso)");
        }

        pending.Add("Nagle off (staged)");
        pending.Add("Monitor 2º: se mantiene (panel vertical)");

        _notify.Notify("🎮 Modo Gaming",
            $"Apps cerradas · Notificaciones en silencio · Alto rendimiento\nPendiente: {string.Join(", ", pending)}");
    }

    public async Task DeactivateAsync(CancellationToken ct = default)
    {
        await _gpu.RestoreAsync();
        await _silencer.RestoreAsync();
        await _power.RestoreAsync();
    }
}
