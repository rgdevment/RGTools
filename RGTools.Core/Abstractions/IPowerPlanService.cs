namespace RGTools.App.Core;

public interface IPowerPlanService
{
    Task ApplyHighPerformanceAsync();

    Task RestoreAsync();
}
