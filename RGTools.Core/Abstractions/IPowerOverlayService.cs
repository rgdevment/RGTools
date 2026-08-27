namespace RGTools.App.Core;

public enum PowerOverlay
{
    Recommended,
    BestEfficiency,
    BestPerformance
}

public interface IPowerOverlayService
{
    PowerOverlay ReadActive();

    Task<bool> ApplyAsync(PowerOverlay overlay);

    Task MigrateToBaselineAsync();
}
