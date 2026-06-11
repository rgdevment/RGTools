namespace RGTools.App.Core;

internal static class ModeRestore
{
    // Each restore step runs even if a previous one throws: a partial failure must not
    // leave the system half-reverted by aborting the remaining steps.
    public static async Task<bool> TryAsync(Func<Task> action, string scope, string label)
    {
        try
        {
            await action().ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            LogService.Log($"[{scope}] Restore step '{label}' failed", ex);
            return false;
        }
    }
}
