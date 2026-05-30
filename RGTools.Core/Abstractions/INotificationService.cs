namespace RGTools.App.Core;

public enum NotificationLevel
{
    Info,
    Warning,
    Critical
}

public interface INotificationService
{
    NotificationLevel MinimumLevel { get; set; }

    void Notify(string title, string message, NotificationLevel level = NotificationLevel.Info);
}
