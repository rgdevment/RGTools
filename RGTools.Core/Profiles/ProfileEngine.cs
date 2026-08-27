namespace RGTools.App.Core;

public sealed class ProfileEngine : IProfileEngine, IDisposable
{
    private const string GpuConsentId = "gaming.gpu-priority";

    private readonly IPowerOverlayService _overlay;
    private readonly IWorkloadGuard _workload;
    private readonly IGamingTweaksService _tweaks;
    private readonly INotificationSilencer _silencer;
    private readonly IGpuPriorityService _gpu;
    private readonly IUserConsentService _consent;
    private readonly IConfigService _config;
    private readonly ISystemStateStore _store;
    private readonly INotificationService _notify;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ProfileKind Active { get; private set; }

    public bool IsApplying { get; private set; }

    public event Action<ProfileKind>? ProfileChanged;

    public event Action<ProfileDrift>? DriftDetected;

    public ProfileEngine(
        IPowerOverlayService overlay,
        IWorkloadGuard workload,
        IGamingTweaksService tweaks,
        INotificationSilencer silencer,
        IGpuPriorityService gpu,
        IUserConsentService consent,
        IConfigService config,
        ISystemStateStore store,
        INotificationService notify)
    {
        _overlay = overlay;
        _workload = workload;
        _tweaks = tweaks;
        _silencer = silencer;
        _gpu = gpu;
        _consent = consent;
        _config = config;
        _store = store;
        _notify = notify;

        Active = config.Current.ActiveProfile;
    }

    // No early return when target == Active. Reapplying is the only way to recover from drift, and
    // every layer below is idempotent precisely so this stays safe to call at any time.
    public async Task ApplyAsync(ProfileKind target, CancellationToken ct = default)
    {
        var profile = ProfileCatalog.For(target);

        // Taken without waiting rather than testing a flag first: a check outside the lock lets two
        // callers through, and they then run their transitions back to back.
        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false))
        {
            LogService.Log($"[PROFILE] Busy, ignoring request for {profile.DisplayName}.");
            return;
        }

        try
        {
            IsApplying = true;
            LogService.Log($"[PROFILE] Applying {profile.DisplayName}.");

            await _config.UpdateAsync(s => s with { ActiveProfile = target }).ConfigureAwait(false);
            await ApplyLayersAsync(profile, ct).ConfigureAwait(false);

            Active = target;
            _notify.MinimumLevel = profile.MinimumNotificationLevel;
            ProfileChanged?.Invoke(target);
            _notify.Notify($"{Icon(target)} Perfil {profile.DisplayName}", profile.Summary);

            LogService.Log($"[PROFILE] {profile.DisplayName} active.");
        }
        catch (Exception ex)
        {
            LogService.LogCrash($"[PROFILE] Applying {profile.DisplayName} failed", ex);

            if (target != ProfileKind.Balanced)
                await ApplyLayersAsync(ProfileCatalog.Balanced, ct).ConfigureAwait(false);

            Active = ProfileKind.Balanced;
            await _config.UpdateAsync(s => s with { ActiveProfile = ProfileKind.Balanced }).ConfigureAwait(false);
            ProfileChanged?.Invoke(ProfileKind.Balanced);
        }
        finally
        {
            IsApplying = false;
            _gate.Release();
        }
    }

    public ProfileDrift Inspect()
    {
        var expected = ProfileCatalog.For(Active);

        var drift = new ProfileDrift
        {
            Expected = Active,
            ExpectedOverlay = expected.Overlay,
            ActualOverlay = _overlay.ReadActive()
        };

        if (drift.HasDrift)
        {
            LogService.Log($"[PROFILE] Drift: {expected.DisplayName} expects {drift.ExpectedOverlay}, system reports {drift.ActualOverlay}.");
            DriftDetected?.Invoke(drift);
        }

        return drift;
    }

    public async Task RestoreSessionAsync(CancellationToken ct = default)
    {
        bool previousRunCrashed = _store.Exists(StateKeys.RunMarker);

        // Written before anything is touched: a crash mid-apply must be visible on the next launch.
        try { await _store.SaveAsync(StateKeys.RunMarker, true).ConfigureAwait(false); }
        catch (Exception ex) { LogService.Log("[PROFILE] Could not write run marker", ex); }

        var target = previousRunCrashed ? ProfileKind.Balanced : _config.Current.ActiveProfile;

        LogService.Log(previousRunCrashed
            ? "[PROFILE] Previous session did not exit cleanly -> resetting to Equilibrado."
            : $"[PROFILE] Reapplying {ProfileCatalog.For(target).DisplayName} from previous session.");

        // Reapplied, not just announced: the machine may have rebooted, and the services Gaming
        // stopped come back on their own, so trusting the stored profile is how state drifts.
        await ApplyAsync(target, ct).ConfigureAwait(false);
    }

    public void MarkCleanShutdown() => _store.Clear(StateKeys.RunMarker);

    private async Task ApplyLayersAsync(ProfileDefinition profile, CancellationToken ct)
    {
        await ProfileLayer.TryAsync(() => _overlay.ApplyAsync(profile.Overlay), "overlay").ConfigureAwait(false);

        await ProfileLayer.TryAsync(
            () => profile.GamingTweaks ? _tweaks.ApplyAsync() : _tweaks.RestoreAsync(),
            "tweaks").ConfigureAwait(false);

        await ProfileLayer.TryAsync(
            () => profile.SilenceNotifications ? _silencer.SilenceAsync() : _silencer.RestoreAsync(),
            "notifications").ConfigureAwait(false);

        await ProfileLayer.TryAsync(() => ApplyGpuAsync(profile), "gpu").ConfigureAwait(false);
        await ProfileLayer.TryAsync(() => ApplyAppsAsync(profile, ct), "apps").ConfigureAwait(false);
    }

    private async Task ApplyGpuAsync(ProfileDefinition profile)
    {
        if (!profile.GpuPriority)
        {
            await _gpu.RestoreAsync().ConfigureAwait(false);
            return;
        }

        if (await _consent.RequestAsync(GpuConsentId,
                "Perfil Juego — GPU Priority",
                "¿Aplicar prioridad de GPU en el registro de Windows? Se revierte al salir del perfil.").ConfigureAwait(false))
            await _gpu.ApplyAsync().ConfigureAwait(false);
        else
            await _gpu.RestoreAsync().ConfigureAwait(false);
    }

    private async Task ApplyAppsAsync(ProfileDefinition profile, CancellationToken ct)
    {
        if (profile.Apps == AppPolicy.GamingHybrid)
        {
            // Captured once, so a reapply never overwrites the original state with the suspended one.
            if (!_store.Exists(StateKeys.Workload))
            {
                var snapshot = await _workload.CaptureAsync(ct).ConfigureAwait(false);
                await _store.SaveAsync(StateKeys.Workload, snapshot).ConfigureAwait(false);
            }

            await _workload.SuspendAsync(ct).ConfigureAwait(false);
            return;
        }

        var saved = _store.Exists(StateKeys.Workload)
            ? await _store.LoadAsync<WorkloadSnapshot>(StateKeys.Workload).ConfigureAwait(false)
            : null;

        await _workload.RestoreAsync(saved, ct).ConfigureAwait(false);
        _store.Clear(StateKeys.Workload);
    }

    private static string Icon(ProfileKind kind) => kind switch
    {
        ProfileKind.Work => "💼",
        ProfileKind.Gaming => "🎮",
        _ => "⚖️"
    };

    public void Dispose() => _gate.Dispose();
}
