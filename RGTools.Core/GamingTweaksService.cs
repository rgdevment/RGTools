using Microsoft.Win32;

namespace RGTools.App.Core;

public sealed class GamingTweaksService : IGamingTweaksService
{
    private const string SystemProfilePath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    private const string GameBarPath = @"Software\Microsoft\GameBar";
    private const string InterfacesPath = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";

    private const int NetworkThrottlingOff = unchecked((int)0xFFFFFFFF);

    private readonly ISystemStateStore _store;

    public GamingTweaksService(ISystemStateStore store) => _store = store;

    public async Task ApplyAsync()
    {
        if (_store.Exists(StateKeys.Tweaks)) return;

        try
        {
            var interfaces = ActiveInterfaceKeys();
            var snapshot = ReadSnapshot(interfaces);
            await _store.SaveAsync(StateKeys.Tweaks, snapshot);

            SetLocalMachineDword(SystemProfilePath, "SystemResponsiveness", 0);
            SetLocalMachineDword(SystemProfilePath, "NetworkThrottlingIndex", NetworkThrottlingOff);
            SetCurrentUserDword(GameBarPath, "AutoGameModeEnabled", 1);

            foreach (var iface in interfaces)
            {
                SetLocalMachineDword($@"{InterfacesPath}\{iface}", "TcpAckFrequency", 1);
                SetLocalMachineDword($@"{InterfacesPath}\{iface}", "TCPNoDelay", 1);
            }

            LogService.Log("[TWEAKS] Gaming tweaks applied (responsiveness, throttling off, Nagle off, Game Mode).");
        }
        catch (Exception ex)
        {
            LogService.Log("[TWEAKS] Apply failed", ex);
        }
    }

    public async Task RestoreAsync()
    {
        if (!_store.Exists(StateKeys.Tweaks)) return;

        try
        {
            var snapshot = await _store.LoadAsync<TweaksSnapshot>(StateKeys.Tweaks);
            if (snapshot != null)
            {
                WriteOrDeleteLocalMachine(SystemProfilePath, "SystemResponsiveness", snapshot.SystemResponsiveness);
                WriteOrDeleteLocalMachine(SystemProfilePath, "NetworkThrottlingIndex", snapshot.NetworkThrottlingIndex);
                WriteOrDeleteCurrentUser(GameBarPath, "AutoGameModeEnabled", snapshot.AutoGameMode);

                foreach (var nic in snapshot.Nagle)
                {
                    WriteOrDeleteLocalMachine(nic.InterfacePath, "TcpAckFrequency", nic.TcpAckFrequency);
                    WriteOrDeleteLocalMachine(nic.InterfacePath, "TCPNoDelay", nic.TcpNoDelay);
                }
            }

            _store.Clear(StateKeys.Tweaks);
            LogService.Log("[TWEAKS] Gaming tweaks restored.");
        }
        catch (Exception ex)
        {
            LogService.Log("[TWEAKS] Restore failed", ex);
        }
    }

    private static TweaksSnapshot ReadSnapshot(IReadOnlyList<string> interfaces)
    {
        var nagle = new List<NagleSnapshot>();
        foreach (var iface in interfaces)
        {
            string path = $@"{InterfacesPath}\{iface}";
            nagle.Add(new NagleSnapshot
            {
                InterfacePath = path,
                TcpAckFrequency = ReadLocalMachineDword(path, "TcpAckFrequency"),
                TcpNoDelay = ReadLocalMachineDword(path, "TCPNoDelay")
            });
        }

        return new TweaksSnapshot
        {
            SystemResponsiveness = ReadLocalMachineDword(SystemProfilePath, "SystemResponsiveness"),
            NetworkThrottlingIndex = ReadLocalMachineDword(SystemProfilePath, "NetworkThrottlingIndex"),
            AutoGameMode = ReadCurrentUserDword(GameBarPath, "AutoGameModeEnabled"),
            Nagle = nagle
        };
    }

    private static List<string> ActiveInterfaceKeys()
    {
        var result = new List<string>();

        using var root = Registry.LocalMachine.OpenSubKey(InterfacesPath, writable: false);
        if (root == null) return result;

        foreach (var name in root.GetSubKeyNames())
        {
            using var iface = root.OpenSubKey(name, writable: false);
            if (iface == null) continue;

            bool hasStaticIp = iface.GetValue("IPAddress") is string[] { Length: > 0 } ip && !string.IsNullOrWhiteSpace(ip[0]) && ip[0] != "0.0.0.0";
            bool hasDhcpIp = iface.GetValue("DhcpIPAddress") is string dhcp && !string.IsNullOrWhiteSpace(dhcp) && dhcp != "0.0.0.0";

            if (hasStaticIp || hasDhcpIp) result.Add(name);
        }

        return result;
    }

    private static int? ReadLocalMachineDword(string path, string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey(path, writable: false);
        return key?.GetValue(name) as int?;
    }

    private static int? ReadCurrentUserDword(string path, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path, writable: false);
        return key?.GetValue(name) as int?;
    }

    private static void SetLocalMachineDword(string path, string name, int value)
    {
        using var key = Registry.LocalMachine.CreateSubKey(path, writable: true);
        key?.SetValue(name, value, RegistryValueKind.DWord);
    }

    private static void SetCurrentUserDword(string path, string name, int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(path, writable: true);
        key?.SetValue(name, value, RegistryValueKind.DWord);
    }

    private static void WriteOrDeleteLocalMachine(string path, string name, int? value)
    {
        using var key = Registry.LocalMachine.CreateSubKey(path, writable: true);
        if (key == null) return;
        if (value == null) key.DeleteValue(name, throwOnMissingValue: false);
        else key.SetValue(name, value, RegistryValueKind.DWord);
    }

    private static void WriteOrDeleteCurrentUser(string path, string name, int? value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(path, writable: true);
        if (key == null) return;
        if (value == null) key.DeleteValue(name, throwOnMissingValue: false);
        else key.SetValue(name, value, RegistryValueKind.DWord);
    }

    private record TweaksSnapshot
    {
        public int? SystemResponsiveness { get; init; }
        public int? NetworkThrottlingIndex { get; init; }
        public int? AutoGameMode { get; init; }
        public List<NagleSnapshot> Nagle { get; init; } = new();
    }

    private record NagleSnapshot
    {
        public string InterfacePath { get; init; } = "";
        public int? TcpAckFrequency { get; init; }
        public int? TcpNoDelay { get; init; }
    }
}
