using Microsoft.Win32;

namespace RGTools.App.Core;

public sealed class GpuPriorityService : IGpuPriorityService
{
    private const string GamesTasksPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";

    private readonly ISystemStateStore _store;

    public GpuPriorityService(ISystemStateStore store) => _store = store;

    public async Task ApplyAsync()
    {
        if (_store.Exists(StateKeys.Gpu)) return;

        try
        {
            await _store.SaveAsync(StateKeys.Gpu, ReadSnapshot()).ConfigureAwait(false);

            using var key = Registry.LocalMachine.CreateSubKey(GamesTasksPath, writable: true);
            if (key == null) return;

            key.SetValue("GPU Priority", 8, RegistryValueKind.DWord);
            key.SetValue("Priority", 6, RegistryValueKind.DWord);
            key.SetValue("Scheduling Category", "High", RegistryValueKind.String);
            LogService.Log("[GPU] Priority applied (8/6/High).");
        }
        catch (Exception ex)
        {
            LogService.Log("[GPU] Apply failed", ex);
            throw;
        }
    }

    public async Task RestoreAsync()
    {
        if (!_store.Exists(StateKeys.Gpu)) return;

        try
        {
            var previous = await _store.LoadAsync<GpuSnapshot>(StateKeys.Gpu).ConfigureAwait(false);
            if (previous == null)
            {
                LogService.Log("[GPU] Snapshot missing/corrupt; priority NOT restored, file kept for retry.");
                return;
            }

            using (var key = Registry.LocalMachine.CreateSubKey(GamesTasksPath, writable: true))
            {
                if (key != null)
                {
                    WriteOrDelete(key, "GPU Priority", previous.GpuPriority, RegistryValueKind.DWord);
                    WriteOrDelete(key, "Priority", previous.Priority, RegistryValueKind.DWord);
                    WriteOrDelete(key, "Scheduling Category", previous.SchedulingCategory, RegistryValueKind.String);
                }
            }

            _store.Clear(StateKeys.Gpu);
            LogService.Log("[GPU] Priority restored.");
        }
        catch (Exception ex)
        {
            LogService.Log("[GPU] Restore failed", ex);
        }
    }

    private static GpuSnapshot ReadSnapshot()
    {
        using var key = Registry.LocalMachine.OpenSubKey(GamesTasksPath, writable: false);
        return new GpuSnapshot
        {
            GpuPriority = key?.GetValue("GPU Priority") as int?,
            Priority = key?.GetValue("Priority") as int?,
            SchedulingCategory = key?.GetValue("Scheduling Category") as string
        };
    }

    private static void WriteOrDelete(RegistryKey key, string name, object? value, RegistryValueKind kind)
    {
        if (value == null) key.DeleteValue(name, throwOnMissingValue: false);
        else key.SetValue(name, value, kind);
    }

    private record GpuSnapshot
    {
        public int? GpuPriority { get; init; }
        public int? Priority { get; init; }
        public string? SchedulingCategory { get; init; }
    }
}
