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
            int previous = ReadToastEnabled();
            await _store.SaveAsync(StateKey, previous);

            SetToastEnabled(0);
            LogService.Log("[SILENCER] Windows toast notifications disabled (Do Not Disturb).");
        }
        catch (Exception ex)
        {
            LogService.Log("[SILENCER] Silence failed", ex);
        }
    }

    public async Task RestoreAsync()
    {
        if (!_store.Exists(StateKey)) return;

        try
        {
            int previous = await _store.LoadAsync<int>(StateKey);
            SetToastEnabled(previous);
            _store.Clear(StateKey);
            LogService.Log("[SILENCER] Windows toast notifications restored.");
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
