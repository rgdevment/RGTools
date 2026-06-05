using System.Runtime.InteropServices;

namespace RGTools.App.Core;

public sealed class DisplayRefreshService : IDisplayRefreshService
{
    private const int EnumCurrentSettings = -1;
    private const int DmDisplayFrequency = 0x400000;
    private const int CdsUpdateRegistry = 0x01;
    private const int CdsGlobal = 0x08;
    private const int DispChangeSuccessful = 0;
    private const uint AttachedToDesktop = 0x1;

    private readonly ISystemStateStore _store;

    public DisplayRefreshService(ISystemStateStore store) => _store = store;

    public async Task ApplyMaxAsync()
    {
        if (_store.Exists(StateKeys.Display)) return;

        try
        {
            var previous = new List<DisplayMode>();
            var targets = new List<(string Device, Devmode Current, int Max)>();

            foreach (var device in ActiveDevices())
            {
                if (!TryGetCurrentMode(device, out var current)) continue;

                previous.Add(new DisplayMode { DeviceName = device, Frequency = current.dmDisplayFrequency });

                int max = MaxFrequencyFor(device, current.dmPelsWidth, current.dmPelsHeight);
                if (max > current.dmDisplayFrequency) targets.Add((device, current, max));
            }

            await _store.SaveAsync(StateKeys.Display, previous);

            var changed = new List<string>();
            foreach (var (device, current, max) in targets)
                if (SetFrequency(device, current, max))
                    changed.Add($"{current.dmDisplayFrequency}->{max}Hz");

            LogService.Log($"[DISPLAY] Max refresh applied ({(changed.Count > 0 ? string.Join(", ", changed) : "already at max")}).");
        }
        catch (Exception ex)
        {
            LogService.Log("[DISPLAY] Apply failed", ex);
        }
    }

    public async Task RestoreAsync()
    {
        if (!_store.Exists(StateKeys.Display)) return;

        try
        {
            var previous = await _store.LoadAsync<List<DisplayMode>>(StateKeys.Display);
            bool allHandled = true;

            if (previous != null)
            {
                foreach (var mode in previous)
                {
                    if (!TryGetCurrentMode(mode.DeviceName, out var current))
                    {
                        allHandled = false;
                        LogService.Log($"[DISPLAY] Restore skipped, device absent: {mode.DeviceName}.");
                        continue;
                    }

                    if (current.dmDisplayFrequency != mode.Frequency && !SetFrequency(mode.DeviceName, current, mode.Frequency))
                        allHandled = false;
                }
            }

            if (allHandled) _store.Clear(StateKeys.Display);
            LogService.Log($"[DISPLAY] Refresh restored{(allHandled ? "" : " (partial, snapshot kept for retry)")}.");
        }
        catch (Exception ex)
        {
            LogService.Log("[DISPLAY] Restore failed", ex);
        }
    }

    private static IEnumerable<string> ActiveDevices()
    {
        uint i = 0;
        while (true)
        {
            var dd = new DisplayDevice();
            dd.cb = Marshal.SizeOf(dd);
            if (!EnumDisplayDevices(null, i, ref dd, 0)) break;
            if ((dd.StateFlags & AttachedToDesktop) != 0)
                yield return dd.DeviceName;
            i++;
        }
    }

    private static bool TryGetCurrentMode(string device, out Devmode mode)
    {
        mode = new Devmode { dmSize = (short)Marshal.SizeOf<Devmode>() };
        return EnumDisplaySettings(device, EnumCurrentSettings, ref mode);
    }

    private static int MaxFrequencyFor(string device, int width, int height)
    {
        int max = 0;
        int i = 0;
        var mode = new Devmode { dmSize = (short)Marshal.SizeOf<Devmode>() };
        while (EnumDisplaySettings(device, i, ref mode))
        {
            if (mode.dmPelsWidth == width && mode.dmPelsHeight == height && mode.dmDisplayFrequency > max)
                max = mode.dmDisplayFrequency;
            i++;
        }
        return max;
    }

    private static bool SetFrequency(string device, Devmode current, int frequency)
    {
        current.dmFields = DmDisplayFrequency;
        current.dmDisplayFrequency = frequency;
        int result = ChangeDisplaySettingsEx(device, ref current, IntPtr.Zero, CdsUpdateRegistry | CdsGlobal, IntPtr.Zero);
        if (result != DispChangeSuccessful)
            LogService.Log($"[DISPLAY] Set {frequency}Hz on {device} returned {result}.");
        return result == DispChangeSuccessful;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DisplayDevice lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref Devmode lpDevMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(string lpszDeviceName, ref Devmode lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Devmode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    private sealed record DisplayMode
    {
        public string DeviceName { get; init; } = "";
        public int Frequency { get; init; }
    }
}
