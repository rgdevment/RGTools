namespace RGTools.App.Core;

public interface IHostsBlocker
{
    Task ApplyAsync(IReadOnlyList<string> hosts);

    Task RestoreAsync();
}
