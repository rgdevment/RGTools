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
            var snapshot = await _store.LoadAsync<WorkloadSnapshot>(StateKeys.Workload).ConfigureAwait(false);
            if (snapshot != null)
            {
                if (await ModeRestore.TryAsync(() => _workload.RestoreAsync(snapshot, ct), "WORK", "workload").ConfigureAwait(false))
                    _store.Clear(StateKeys.Workload);
            }
            else
            {
                LogService.Log("[WORK] Workload snapshot missing/corrupt; kept for retry.");
            }
        }

        await ModeRestore.TryAsync(_gpu.RestoreAsync, "WORK", "gpu").ConfigureAwait(false);
        await ModeRestore.TryAsync(_display.RestoreAsync, "WORK", "display").ConfigureAwait(false);
        await ModeRestore.TryAsync(_tweaks.RestoreAsync, "WORK", "tweaks").ConfigureAwait(false);
        await ModeRestore.TryAsync(_silencer.RestoreAsync, "WORK", "silencer").ConfigureAwait(false);
        await ModeRestore.TryAsync(_power.ApplyPowerSaverAsync, "WORK", "power").ConfigureAwait(false);

        _notify.MinimumLevel = NotificationLevel.Info;
        _notify.Notify("💼 Modo Trabajo", "Estado restaurado · plan de ahorro");
    }

    public Task DeactivateAsync(CancellationToken ct = default) => Task.CompletedTask;
}
