namespace RGTools.App.Core;

public sealed class WorkModeService : IMode
{
    public const string GamingStateKey = "gaming-workload";

    private readonly IPowerPlanService _power;
    private readonly IWorkloadGuard _workload;
    private readonly ISystemStateStore _store;
    private readonly INotificationService _notify;

    public WorkModeService(
        IPowerPlanService power,
        IWorkloadGuard workload,
        ISystemStateStore store,
        INotificationService notify)
    {
        _power = power;
        _workload = workload;
        _store = store;
        _notify = notify;
    }

    public ProfileKind Kind => ProfileKind.Work;

    public async Task ActivateAsync(CancellationToken ct = default)
    {
        if (_store.Exists(GamingStateKey))
        {
            var snapshot = await _store.LoadAsync<WorkloadSnapshot>(GamingStateKey);
            if (snapshot != null) await _workload.RestoreAsync(snapshot, ct);
            _store.Clear(GamingStateKey);
        }

        await _power.SetBalancedAsync();
        _notify.Notify("💼 Modo Trabajo", "Plan equilibrado · servicios restaurados");
    }

    public Task DeactivateAsync(CancellationToken ct = default) => Task.CompletedTask;
}
