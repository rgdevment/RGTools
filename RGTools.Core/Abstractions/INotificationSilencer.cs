namespace RGTools.App.Core;

public interface INotificationSilencer
{
    Task SilenceAsync();

    Task RestoreAsync();
}
