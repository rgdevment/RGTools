namespace RGTools.App.Core;

public interface IStartupService
{
    Task<bool> SetStartupAsync(bool enable);

    Task<bool> IsEnabledAsync();
}
