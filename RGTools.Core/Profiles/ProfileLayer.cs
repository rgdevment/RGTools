namespace RGTools.App.Core;

internal static class ProfileLayer
{
    // Every layer runs even if an earlier one throws: a failure in one must not leave the machine
    // half-way between two profiles by aborting the rest.
    public static async Task<bool> TryAsync(Func<Task> action, string label)
    {
        try
        {
            await action().ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            LogService.Log($"[PROFILE] Layer '{label}' failed", ex);
            return false;
        }
    }
}
