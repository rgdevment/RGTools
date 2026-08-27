using Microsoft.Win32;

namespace RGTools.App.Core;

public sealed class GamingTweaksService : IGamingTweaksService
{
    private const string SystemProfilePath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";

    private const int NetworkThrottlingOff = unchecked((int)0xFFFFFFFF);

    // 10, not 0. Zero removes the MMCSS reservation altogether and starves the audio thread, which
    // shows up as dropouts on wireless headsets. 10 is the value Microsoft documents for games.
    private const int GamingResponsiveness = 10;

    private readonly ISystemStateStore _store;

    public GamingTweaksService(ISystemStateStore store) => _store = store;

    public async Task ApplyAsync()
    {
        try
        {
            // Snapshot once, write always: reapplying the profile must repair values something else
            // changed, but must never record the tweaked values as if they were the originals.
            if (!_store.Exists(StateKeys.Tweaks))
                await _store.SaveAsync(StateKeys.Tweaks, ReadSnapshot()).ConfigureAwait(false);

            SetDword(SystemProfilePath, "SystemResponsiveness", GamingResponsiveness);
            SetDword(SystemProfilePath, "NetworkThrottlingIndex", NetworkThrottlingOff);

            LogService.Log("[TWEAKS] Applied (responsiveness 10, network throttling off).");
        }
        catch (Exception ex)
        {
            LogService.Log("[TWEAKS] Apply failed", ex);
            throw;
        }
    }

    public async Task RestoreAsync()
    {
        if (!_store.Exists(StateKeys.Tweaks)) return;

        try
        {
            var snapshot = await _store.LoadAsync<TweaksSnapshot>(StateKeys.Tweaks).ConfigureAwait(false);
            if (snapshot == null)
            {
                // Keep the file so the next attempt retries instead of leaving the tweaks applied forever.
                LogService.Log("[TWEAKS] Snapshot missing/corrupt; not restored, file kept for retry.");
                return;
            }

            WriteOrDelete(SystemProfilePath, "SystemResponsiveness", snapshot.SystemResponsiveness);
            WriteOrDelete(SystemProfilePath, "NetworkThrottlingIndex", snapshot.NetworkThrottlingIndex);

            _store.Clear(StateKeys.Tweaks);
            LogService.Log("[TWEAKS] Restored.");
        }
        catch (Exception ex)
        {
            LogService.Log("[TWEAKS] Restore failed", ex);
        }
    }

    private static TweaksSnapshot ReadSnapshot() => new()
    {
        SystemResponsiveness = ReadDword(SystemProfilePath, "SystemResponsiveness"),
        NetworkThrottlingIndex = ReadDword(SystemProfilePath, "NetworkThrottlingIndex")
    };

    private static int? ReadDword(string path, string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey(path, writable: false);
        return key?.GetValue(name) as int?;
    }

    private static void SetDword(string path, string name, int value)
    {
        using var key = Registry.LocalMachine.CreateSubKey(path, writable: true);
        key?.SetValue(name, value, RegistryValueKind.DWord);
    }

    private static void WriteOrDelete(string path, string name, int? value)
    {
        using var key = Registry.LocalMachine.CreateSubKey(path, writable: true);
        if (key == null) return;

        if (value == null) key.DeleteValue(name, throwOnMissingValue: false);
        else key.SetValue(name, value, RegistryValueKind.DWord);
    }

    private record TweaksSnapshot
    {
        public int? SystemResponsiveness { get; init; }
        public int? NetworkThrottlingIndex { get; init; }
    }
}
