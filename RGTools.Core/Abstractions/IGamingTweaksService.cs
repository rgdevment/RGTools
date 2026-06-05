namespace RGTools.App.Core;

public interface IGamingTweaksService
{
    Task ApplyAsync();

    Task RestoreAsync();
}
