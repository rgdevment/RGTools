using Microsoft.Win32;

namespace RGTools.App.Core;

public sealed class GamingModeService : IMode
{
    private const string GpuStateKey = "gaming-gpu";
    private const string GpuConsentId = "gaming.gpu-priority";
    private const string GamesTasksPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";

    private readonly IPowerPlanService _power;
    private readonly IWorkloadGuard _workload;
    private readonly ISystemStateStore _store;
    private readonly IUserConsentService _consent;
    private readonly INotificationService _notify;

    public GamingModeService(
        IPowerPlanService power,
        IWorkloadGuard workload,
        ISystemStateStore store,
        IUserConsentService consent,
        INotificationService notify)
    {
        _power = power;
        _workload = workload;
        _store = store;
        _consent = consent;
        _notify = notify;
    }

    public ProfileKind Kind => ProfileKind.Gaming;

    public async Task ActivateAsync(CancellationToken ct = default)
    {
        var snapshot = await _workload.SuspendAsync(ct);
        await _store.SaveAsync(WorkModeService.GamingStateKey, snapshot);

        await _power.SetHighPerformanceAsync();

        var pending = new List<string>();

        if (await _consent.RequestAsync(GpuConsentId,
                "Modo Gaming — GPU Priority",
                "¿Aplicar prioridad de GPU en el registro de Windows? Se revierte al salir del modo."))
        {
            await ApplyGpuPriorityAsync();
        }
        else
        {
            pending.Add("GPU Priority (sin permiso)");
        }

        pending.Add("Nagle off (staged: requiere validación por NIC)");
        pending.Add("Apagar monitor secundario (staged: requiere API de display)");

        _notify.Notify("🎮 Modo Gaming",
            $"Carga pausada · Máximo rendimiento\nPendiente: {string.Join(", ", pending)}");
    }

    public async Task DeactivateAsync(CancellationToken ct = default)
    {
        await RestoreGpuPriorityAsync();
    }

    private async Task ApplyGpuPriorityAsync()
    {
        try
        {
            var previous = ReadGpuSnapshot();
            await _store.SaveAsync(GpuStateKey, previous);

            using var key = Registry.LocalMachine.CreateSubKey(GamesTasksPath, writable: true);
            if (key == null) return;

            key.SetValue("GPU Priority", 8, RegistryValueKind.DWord);
            key.SetValue("Priority", 6, RegistryValueKind.DWord);
            key.SetValue("Scheduling Category", "High", RegistryValueKind.String);
            LogService.Log("[GAMING] GPU Priority applied.");
        }
        catch (Exception ex)
        {
            LogService.Log("[GAMING] GPU Priority apply failed", ex);
        }
    }

    private async Task RestoreGpuPriorityAsync()
    {
        if (!_store.Exists(GpuStateKey)) return;

        try
        {
            var previous = await _store.LoadAsync<GpuSnapshot>(GpuStateKey);
            using (var key = Registry.LocalMachine.CreateSubKey(GamesTasksPath, writable: true))
            {
                if (key != null && previous != null)
                {
                    WriteOrDelete(key, "GPU Priority", previous.GpuPriority, RegistryValueKind.DWord);
                    WriteOrDelete(key, "Priority", previous.Priority, RegistryValueKind.DWord);
                    WriteOrDelete(key, "Scheduling Category", previous.SchedulingCategory, RegistryValueKind.String);
                }
            }

            _store.Clear(GpuStateKey);
            LogService.Log("[GAMING] GPU Priority restored.");
        }
        catch (Exception ex)
        {
            LogService.Log("[GAMING] GPU Priority restore failed", ex);
        }
    }

    private static GpuSnapshot ReadGpuSnapshot()
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
