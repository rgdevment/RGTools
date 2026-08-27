using System.Windows;
using H.NotifyIcon;
using Microsoft.Win32;

namespace RGTools.App.Core;

public sealed class NotificationService : INotificationService
{
    private const string PushNotificationsKey = @"Software\Microsoft\Windows\CurrentVersion\PushNotifications";

    private TaskbarIcon? _icon;

    public NotificationLevel MinimumLevel { get; set; } = NotificationLevel.Info;

    public void Attach(TaskbarIcon icon) => _icon = icon;

    public void Notify(string title, string message, NotificationLevel level = NotificationLevel.Info)
    {
        LogService.Log($"[NOTIFY:{level}] {title} — {message}");

        if (level < MinimumLevel) return;

        var app = Application.Current;
        if (app == null) return;

        app.Dispatcher.Invoke(() =>
        {
            try
            {
                // Gaming sets ToastEnabled to 0, which would also swallow RGTools' own warnings.
                // Anything at Warning or above falls back to a dialog that setting cannot suppress.
                if (level >= NotificationLevel.Warning && !ToastsEnabled())
                {
                    MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _icon?.ShowNotification(title, message);
            }
            catch (Exception ex)
            {
                LogService.Log("[NOTIFY] Display failed", ex);
            }
        });
    }

    private static bool ToastsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PushNotificationsKey, writable: false);
            return key?.GetValue("ToastEnabled") as int? != 0;
        }
        catch
        {
            return true;
        }
    }
}
