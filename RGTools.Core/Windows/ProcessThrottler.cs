namespace RGTools.App.Core;

public sealed class ProcessThrottler : IProcessThrottler
{
    public void SetEfficiency(string processName, bool enabled)
    {
        var (applied, failed) = EfficiencyMode.ApplyToAll(processName, enabled);

        if (applied > 0 || failed > 0)
            LogService.Log($"[ECOQOS] {processName}: {(enabled ? "on" : "off")} for {applied} process(es)" +
                           (failed > 0 ? $", {failed} refused." : "."));
    }
}
