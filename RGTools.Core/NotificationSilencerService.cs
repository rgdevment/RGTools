using Microsoft.Win32;

namespace RGTools.App.Core;

public sealed class NotificationSilencerService : INotificationSilencer
{
    private const string StateKey = StateKeys.Toasts;
    private const string SubKey = @"Software\Microsoft\Windows\CurrentVersion\PushNotifications";
    private const string ValueName = "ToastEnabled";

    private readonly ISystemStateStore _store;

    public NotificationSilencerService(ISystemStateStore store) => _store = store;

    public async Task SilenceAsync()
    {
        try
        {
            if (!_store.Exists(StateKey))
                await _store.SaveAsync(StateKey, ReadToastEnabled()).ConfigureAwait(false);

            SetToastEnabled(0);
            LogService.Log("[SILENCER] Windows toast notifications disabled (Do Not Disturb).");
        }
        catch (Exception ex)
        {
            LogService.Log("[SILENCER] Silence failed", ex);
            throw;
        }
    }

    public async Task RestoreAsync()
    {
        if (!_store.Exists(StateKey)) return;

        try
        {
            // int? (not int): a corrupt snapshot deserializes to null, not 0 — and 0 would
            // mean "stay silenced". Default to 1 (enabled) so Work never leaves toasts off.
            int? previous = await _store.LoadAsync<int?>(StateKey).ConfigureAwait(false);
            SetToastEnabled(previous ?? 1);
            _store.Clear(StateKey);
            LogService.Log($"[SILENCER] Windows toast notifications restored ({(previous.HasValue ? previous.Value.ToString() : "default-enabled")}).");
        }
        catch (Exception ex)
        {
            LogService.Log("[SILENCER] Restore failed", ex);
        }
    }

    private static int ReadToastEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SubKey, writable: false);
        return key?.GetValue(ValueName) as int? ?? 1;
    }

    private static void SetToastEnabled(int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(SubKey, writable: true);
        key?.SetValue(ValueName, value, RegistryValueKind.DWord);
    }
}
