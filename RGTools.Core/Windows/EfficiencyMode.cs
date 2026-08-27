using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RGTools.App.Core;

// EcoQoS: the Windows 11 mechanism behind Task Manager's "Efficiency mode". Parks a process on the
// E-cores and drops its clock without closing it, and reverses cleanly — unlike killing the app.
internal static class EfficiencyMode
{
    private const int ProcessPowerThrottling = 4;
    private const uint CurrentVersion = 1;
    private const uint ExecutionSpeed = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessInformation(
        IntPtr hProcess, int informationClass, ref PowerThrottlingState information, uint size);

    public static (int Applied, int Failed) ApplyToAll(string processName, bool enabled)
    {
        int applied = 0, failed = 0;

        try
        {
            // Electron apps (Slack, Discord, WhatsApp) run many processes under one name; all of them count.
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    if (TryApply(process, enabled)) applied++;
                    else failed++;
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Log($"[ECOQOS] Enumerating '{processName}' failed", ex);
        }

        return (applied, failed);
    }

    private static bool TryApply(Process process, bool enabled)
    {
        try
        {
            var state = new PowerThrottlingState
            {
                Version = CurrentVersion,
                ControlMask = ExecutionSpeed,
                StateMask = enabled ? ExecutionSpeed : 0
            };

            if (!SetProcessInformation(process.Handle, ProcessPowerThrottling, ref state,
                    (uint)Marshal.SizeOf<PowerThrottlingState>()))
                return false;

            // Throttling alone is not what Task Manager shows as efficiency mode; the scheduler also
            // needs the low priority class, and it is what actually keeps the app off the P-cores.
            process.PriorityClass = enabled ? ProcessPriorityClass.Idle : ProcessPriorityClass.Normal;
            return true;
        }
        catch
        {
            // Services running as SYSTEM (SearchIndexer) can refuse the handle even to an admin.
            return false;
        }
    }
}
