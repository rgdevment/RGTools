namespace RGTools.App.Core;

public sealed class ModeManager : IModeManager
{
    private readonly Dictionary<ProfileKind, IMode> _modes;
    private readonly IConfigService _config;
    private readonly ISystemStateStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ProfileKind Active { get; private set; } = ProfileKind.Work;

    public bool IsTransitioning { get; private set; }

    public bool IsDirty => StateKeys.All.Any(_store.Exists);

    public event Action<ProfileKind>? ModeChanged;

    public ModeManager(IEnumerable<IMode> modes, IConfigService config, ISystemStateStore store)
    {
        _modes = modes.ToDictionary(m => m.Kind);
        _config = config;
        _store = store;
        Active = config.Current.ActiveProfile;
    }

    public async Task SwitchToAsync(ProfileKind target, CancellationToken ct = default)
    {
        if (!_modes.ContainsKey(target))
        {
            LogService.Log($"[MODE] Unknown target {target}, ignoring.");
            return;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (target == Active) return;

            IsTransitioning = true;
            LogService.Log($"[MODE] Switching {Active} -> {target}");

            await _config.SaveAsync(_config.Current with { ActiveProfile = target });

            if (_modes.TryGetValue(Active, out var current))
                await current.DeactivateAsync(ct);

            await _modes[target].ActivateAsync(ct);

            Active = target;
            ModeChanged?.Invoke(target);
            LogService.Log($"[MODE] Active mode is now {target}");
        }
        catch (Exception ex)
        {
            LogService.LogCrash($"[MODE] Transition to {target} failed", ex);
            await ForceWorkAsync(ct);
        }
        finally
        {
            IsTransitioning = false;
            _gate.Release();
        }
    }

    public async Task SanitizeToWorkAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            IsTransitioning = true;
            await ForceWorkAsync(ct);
        }
        finally
        {
            IsTransitioning = false;
            _gate.Release();
        }
    }

    private async Task ForceWorkAsync(CancellationToken ct)
    {
        try
        {
            await _config.SaveAsync(_config.Current with { ActiveProfile = ProfileKind.Work });
            await _modes[ProfileKind.Work].ActivateAsync(ct);
            Active = ProfileKind.Work;
            ModeChanged?.Invoke(ProfileKind.Work);
            LogService.Log("[MODE] Forced clean Work state.");
        }
        catch (Exception ex)
        {
            LogService.LogCrash("[MODE] Force Work failed", ex);
        }
    }
}
