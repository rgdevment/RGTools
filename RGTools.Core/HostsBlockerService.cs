using System.IO;

namespace RGTools.App.Core;

public sealed class HostsBlockerService : IHostsBlocker
{
    private const string Marker = "# RGTools-Zen";
    private static readonly string HostsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");

    private readonly ISystemStateStore _store;

    public HostsBlockerService(ISystemStateStore store) => _store = store;

    public async Task ApplyAsync(IReadOnlyList<string> hosts)
    {
        if (hosts.Count == 0) return;

        try
        {
            await StripMarkedLinesAsync();

            var lines = hosts.Select(h => $"127.0.0.1 {h} {Marker}");
            await AppendAtomicAsync(string.Join(Environment.NewLine, lines) + Environment.NewLine);
            await _store.SaveAsync(StateKeys.ZenHosts, true);
            LogService.Log($"[HOSTS] Blocked {hosts.Count} host(s).");
        }
        catch (Exception ex)
        {
            LogService.Log("[HOSTS] Block failed", ex);
        }
    }

    public async Task RestoreAsync()
    {
        if (!_store.Exists(StateKeys.ZenHosts)) return;

        try
        {
            await StripMarkedLinesAsync();
            _store.Clear(StateKeys.ZenHosts);
            LogService.Log("[HOSTS] Restored (marked lines removed).");
        }
        catch (Exception ex)
        {
            LogService.Log("[HOSTS] Restore failed", ex);
        }
    }

    private static async Task StripMarkedLinesAsync()
    {
        if (!File.Exists(HostsPath)) return;

        var lines = await File.ReadAllLinesAsync(HostsPath);
        var kept = lines.Where(l => !l.Contains(Marker, StringComparison.OrdinalIgnoreCase)).ToArray();

        if (kept.Length != lines.Length)
            await WriteAtomicAsync(string.Join(Environment.NewLine, kept) + Environment.NewLine);
    }

    private static async Task AppendAtomicAsync(string toAppend)
    {
        string existing = File.Exists(HostsPath) ? await File.ReadAllTextAsync(HostsPath) : string.Empty;
        if (existing.Length > 0 && !existing.EndsWith('\n')) existing += Environment.NewLine;
        await WriteAtomicAsync(existing + toAppend);
    }

    private static async Task WriteAtomicAsync(string content)
    {
        string tmp = HostsPath + ".rgtools.tmp";
        await File.WriteAllTextAsync(tmp, content);
        File.Move(tmp, HostsPath, overwrite: true);
    }
}
