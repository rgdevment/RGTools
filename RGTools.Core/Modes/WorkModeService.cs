namespace RGTools.App.Core;

public sealed class WorkModeService : IMode
{
    private readonly IWorkloadGuard _workload;
    private readonly IGpuPriorityService _gpu;
    private readonly IDisplayRefreshService _display;
    private readonly IGamingTweaksService _tweaks;
    private readonly INotificationSilencer _silencer;
    private readonly IPowerPlanService _power;
    private readonly ISystemStateStore _store;
    private readonly INotificationService _notify;

    public WorkModeService(
        IWorkloadGuard workload,
        IGpuPriorityService gpu,
        IDisplayRefreshService display,
        IGamingTweaksService tweaks,
        INotificationSilencer silencer,
        IPowerPlanService power,
        ISystemStateStore store,
        INotificationService notify)
    {
        _workload = workload;
        _gpu = gpu;
        _display = display;
        _tweaks = tweaks;
        _silencer = silencer;
        _power = power;
        _store = store;
        _notify = notify;
    }

    public ProfileKind Kind => ProfileKind.Work;

    public async Task ActivateAsync(CancellationToken ct = default)
    {
        if (_store.Exists(StateKeys.Workload))
        {
            var snapshot = await _store.LoadAsync<WorkloadSnapshot>(StateKeys.Workload);
            if (snapshot != null) await _workload.RestoreAsync(snapshot, ct);
            _store.Clear(StateKeys.Workload);
        }

        await _gpu.RestoreAsync();
        await _display.RestoreAsync();
        await _tweaks.RestoreAsync();
        await _silencer.RestoreAsync();
        await _power.RestoreAsync();

        _notify.MinimumLevel = NotificationLevel.Info;
        _notify.Notify("💼 Modo Trabajo", "Estado restaurado · plan equilibrado");
    }

    public Task DeactivateAsync(CancellationToken ct = default) => Task.CompletedTask;
}
