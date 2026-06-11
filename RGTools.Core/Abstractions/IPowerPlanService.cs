namespace RGTools.App.Core;

public interface IPowerPlanService
{
    Task ApplyHighPerformanceAsync();

    Task ApplyPowerSaverAsync();

    Task RestoreAsync();
}
