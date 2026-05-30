namespace RGTools.App.Core;

public interface IPowerPlanService
{
    Task SetBalancedAsync();

    Task SetHighPerformanceAsync();
}
