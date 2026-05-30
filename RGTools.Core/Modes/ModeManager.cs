namespace RGTools.App.Core;

public sealed class ModeManager : IModeManager
{
    private readonly Dictionary<ProfileKind, IMode> _modes;
    private readonly IConfigService _config;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ProfileKind Active { get; private set; } = ProfileKind.Work;

    public bool IsTransitioning { get; private set; }

    public event Action<ProfileKind>? ModeChanged;

    public ModeManager(IEnumerable<IMode> modes, IConfigService config)
    {
        _modes = modes.ToDictionary(m => m.Kind);
        _config = config;
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

            if (_modes.TryGetValue(Active, out var current))
                await current.DeactivateAsync(ct);

            await _modes[target].ActivateAsync(ct);

            Active = target;
            await _config.SaveAsync(_config.Current with { ActiveProfile = target });

            ModeChanged?.Invoke(target);
            LogService.Log($"[MODE] Active mode is now {target}");
        }
        catch (Exception ex)
        {
            LogService.LogCrash($"[MODE] Transition to {target} failed", ex);
            await RecoverToWorkAsync(ct);
        }
        finally
        {
            IsTransitioning = false;
            _gate.Release();
        }
    }

    private async Task RecoverToWorkAsync(CancellationToken ct)
    {
        if (Active == ProfileKind.Work || !_modes.TryGetValue(ProfileKind.Work, out var work)) return;

        try
        {
            LogService.Log("[MODE] Recovering to Work after failed transition.");
            await work.ActivateAsync(ct);
            Active = ProfileKind.Work;
            await _config.SaveAsync(_config.Current with { ActiveProfile = ProfileKind.Work });
            ModeChanged?.Invoke(ProfileKind.Work);
        }
        catch (Exception ex)
        {
            LogService.LogCrash("[MODE] Recovery to Work failed", ex);
        }
    }
}
