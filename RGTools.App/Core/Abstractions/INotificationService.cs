namespace RGTools.App.Core;

public enum NotificationLevel
{
    Info,
    Warning,
    Critical
}

public interface INotificationService
{
    void Notify(string title, string message, NotificationLevel level = NotificationLevel.Info);
}
