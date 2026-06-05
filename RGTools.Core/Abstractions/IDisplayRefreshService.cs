namespace RGTools.App.Core;

public interface IDisplayRefreshService
{
    Task ApplyMaxAsync();

    Task RestoreAsync();
}
