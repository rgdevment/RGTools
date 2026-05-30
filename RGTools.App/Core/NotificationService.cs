using System.Windows;
using H.NotifyIcon;

namespace RGTools.App.Core;

public sealed class NotificationService : INotificationService
{
    private TaskbarIcon? _icon;

    public void Attach(TaskbarIcon icon) => _icon = icon;

    public void Notify(string title, string message, NotificationLevel level = NotificationLevel.Info)
    {
        LogService.Log($"[NOTIFY:{level}] {title} — {message}");

        var app = Application.Current;
        if (app == null) return;

        app.Dispatcher.Invoke(() =>
        {
            try
            {
                _icon?.ShowNotification(title, message);
            }
            catch (Exception ex)
            {
                LogService.Log("[NOTIFY] Display failed", ex);
            }
        });
    }
}
