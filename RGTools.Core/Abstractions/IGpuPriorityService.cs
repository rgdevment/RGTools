namespace RGTools.App.Core;

public interface IGpuPriorityService
{
    Task ApplyAsync();

    Task RestoreAsync();
}
