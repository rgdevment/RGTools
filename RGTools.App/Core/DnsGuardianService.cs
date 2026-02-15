using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace RGTools.App.Core;

public class DnsGuardianService : IDisposable
{
    private const string TargetDns = "192.168.50.100";
    private const int CheckIntervalMinutes = 5;
    private const bool EnableDohEncryption = false; // Set to true to enable DNS-over-HTTPS encryption
    private const bool EnableStrictMode = true; // Set to true to block all DNS traffic except through TargetDns

    private static readonly string? TargetDohTemplate = Environment.GetEnvironmentVariable("PERSONAL_DOH", EnvironmentVariableTarget.Machine);
    private static readonly string[] KnownDohDotServers = [
        "8.8.8.8", "8.8.4.4",           // Google DNS
        "1.1.1.1", "1.0.0.1",           // Cloudflare
        "9.9.9.9", "149.112.112.112",   // Quad9
        "208.67.222.222", "208.67.220.220", // OpenDNS
        "94.140.14.14", "94.140.15.15", // AdGuard DNS
        "76.76.2.0", "76.76.10.0",      // Control D
        "185.228.168.9", "185.228.169.9", // CleanBrowsing
        "45.90.28.0", "45.90.30.0"      // NextDNS
    ];

    private CancellationTokenSource? _cts;
    private ManagementEventWatcher? _networkWatcher;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _firewallRulesApplied = false;

    public bool IsRunning => _cts != null;
    public event Action<bool>? StatusChanged;

    public void Start()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();

        if (EnableStrictMode)
        {
            ApplyFirewallRules();
        }

        StartWmiListener();
        Task.Run(() => LoopAsync(_cts.Token));

        StatusChanged?.Invoke(true);
        LogService.Log("[Guardian] Service Started.");
    }

    public void Stop()
    {
        if (_cts == null) return;

        _cts.Cancel();
        _cts.Dispose();
        _cts = null;

        _networkWatcher?.Stop();
        _networkWatcher?.Dispose();
        _networkWatcher = null;

        if (_firewallRulesApplied)
        {
            RemoveFirewallRules();
        }

        StatusChanged?.Invoke(false);
        LogService.Log("[Guardian] Service Stopped.");
    }

    private void StartWmiListener()
    {
        try
        {
            var query = new WqlEventQuery("__InstanceModificationEvent",
                TimeSpan.FromSeconds(2),
                "TargetInstance ISA 'Win32_NetworkAdapterConfiguration'");

            _networkWatcher = new ManagementEventWatcher(query);
            _networkWatcher.EventArrived += async (s, e) =>
            {
                await Task.Delay(2000);
                await CheckAndRestoreDnsAsync("WmiEvent");
            };
            _networkWatcher.Start();
        }
        catch (Exception ex)
        {
            LogService.Log($"[Guardian] WMI Error: {ex.Message}");
        }
    }

    private async Task LoopAsync(CancellationToken token)
    {
        try
        {
            await CheckAndRestoreDnsAsync("Startup");

            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(CheckIntervalMinutes));
            while (await timer.WaitForNextTickAsync(token))
            {
                await CheckAndRestoreDnsAsync("Timer");
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task CheckAndRestoreDnsAsync(string source)
    {
        if (!await _lock.WaitAsync(0)) return;

        try
        {
            var nic = GetPhysicalInterface();
            if (nic == null) return;

            var ipProps = nic.GetIPProperties();
            var dnsAddresses = ipProps.DnsAddresses
                .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork)
                .Select(ip => ip.ToString())
                .ToList();

            if (dnsAddresses.FirstOrDefault() != TargetDns)
            {
                LogService.Log($"[Guardian] ({source}) HIJACK DETECTED! Restoring {TargetDns}...");
                await RestoreDnsIpAsync(nic.Name);
            }
        }
        catch (Exception ex)
        {
            LogService.Log($"[Guardian] Error: {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task RestoreDnsIpAsync(string interfaceName)
    {
        // Future-self: Critical - restore IP immediately
        await RunProcessAsync("netsh", $"interface ip set dns name=\"{interfaceName}\" static {TargetDns} validate=no");

        if (EnableDohEncryption && !string.IsNullOrEmpty(TargetDohTemplate))
        {
            string psScript = """
                $dns = '[DNS]';
                $template = '[TEMPLATE]';
                $alias = '[INTERFACE]';

                # Register DoH
                if (-not (Get-DnsClientDohServerAddress -ServerAddress $dns -ErrorAction SilentlyContinue)) {
                    Add-DnsClientDohServerAddress -ServerAddress $dns -DohTemplate $template -AllowFallbackToUdp $true -AutoUpgrade $true
                } else {
                    Set-DnsClientDohServerAddress -ServerAddress $dns -DohTemplate $template -AllowFallbackToUdp $true -AutoUpgrade $true
                }

                # Force Windows to re-evaluate the interface DNS
                Set-DnsClientServerAddress -InterfaceAlias $alias -ServerAddresses $dns

                # Wake up the DoH client by performing a test resolution
                Clear-DnsClientCache
                Resolve-DnsName google.com -Server $dns -Type A -DnsOnly -ErrorAction SilentlyContinue
                """
                .Replace("[DNS]", TargetDns)
                .Replace("[TEMPLATE]", TargetDohTemplate)
                .Replace("[INTERFACE]", interfaceName);

            await RunProcessAsync("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{psScript}\"");
            LogService.Log($"[Guardian] DNS + DoH Restore attempted for '{interfaceName}'.");
        }
    }

    private NetworkInterface? GetPhysicalInterface()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni =>
                ni.OperationalStatus == OperationalStatus.Up &&
                (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                 ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) &&
                ni.GetIPProperties().GatewayAddresses.Count > 0 &&
                ni.NetworkInterfaceType != NetworkInterfaceType.Loopback
            )
            .OrderByDescending(ni => ni.Speed)
            .FirstOrDefault();
    }

    private async Task RunProcessAsync(string fileName, string args)
    {
        try
        {
            ProcessStartInfo psi = new(fileName, args)
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };

            using Process? p = Process.Start(psi);
            if (p != null) await p.WaitForExitAsync();
        }
        catch (Exception ex)
        {
            LogService.Log($"[Guardian] Process Error: {ex.Message}");
        }
    }

    private void ApplyFirewallRules()
    {
        try
        {
            // Allow outbound DNS to our protected server (high priority)
            string allowRule = $"netsh advfirewall firewall add rule name=\"RGTools-DNS-Guardian-Allow\" dir=out action=allow protocol=UDP remoteip={TargetDns} remoteport=53";
            RunProcessAsync("cmd.exe", $"/c {allowRule}").Wait();

            string allowRuleTcp = $"netsh advfirewall firewall add rule name=\"RGTools-DNS-Guardian-Allow-TCP\" dir=out action=allow protocol=TCP remoteip={TargetDns} remoteport=53";
            RunProcessAsync("cmd.exe", $"/c {allowRuleTcp}").Wait();

            // Block all other outbound DNS traffic
            string blockRule = "netsh advfirewall firewall add rule name=\"RGTools-DNS-Guardian-Block\" dir=out action=block protocol=UDP remoteport=53";
            RunProcessAsync("cmd.exe", $"/c {blockRule}").Wait();

            string blockRuleTcp = "netsh advfirewall firewall add rule name=\"RGTools-DNS-Guardian-Block-TCP\" dir=out action=block protocol=TCP remoteport=53";
            RunProcessAsync("cmd.exe", $"/c {blockRuleTcp}").Wait();

            // Block DNS-over-TLS (DoT) - Port 853
            string blockDoT = "netsh advfirewall firewall add rule name=\"RGTools-DNS-Guardian-Block-DoT\" dir=out action=block protocol=TCP remoteport=853";
            RunProcessAsync("cmd.exe", $"/c {blockDoT}").Wait();

            // Block known DoH/DoT servers on port 443 (DoH uses HTTPS)
            foreach (var server in KnownDohDotServers)
            {
                string blockDoh = $"netsh advfirewall firewall add rule name=\"RGTools-DNS-Guardian-Block-DoH-{server.Replace(".", "-")}\" dir=out action=block protocol=TCP remoteip={server} remoteport=443";
                RunProcessAsync("cmd.exe", $"/c {blockDoh}").Wait();
            }

            _firewallRulesApplied = true;
            LogService.Log($"[Guardian] Strict Mode ENABLED - All DNS/DoH/DoT blocked except {TargetDns}");
        }
        catch (Exception ex)
        {
            LogService.Log($"[Guardian] Firewall Error: {ex.Message}");
        }
    }

    private void RemoveFirewallRules()
    {
        try
        {
            RunProcessAsync("cmd.exe", "/c netsh advfirewall firewall delete rule name=\"RGTools-DNS-Guardian-Allow\"").Wait();
            RunProcessAsync("cmd.exe", "/c netsh advfirewall firewall delete rule name=\"RGTools-DNS-Guardian-Allow-TCP\"").Wait();
            RunProcessAsync("cmd.exe", "/c netsh advfirewall firewall delete rule name=\"RGTools-DNS-Guardian-Block\"").Wait();
            RunProcessAsync("cmd.exe", "/c netsh advfirewall firewall delete rule name=\"RGTools-DNS-Guardian-Block-TCP\"").Wait();
            RunProcessAsync("cmd.exe", "/c netsh advfirewall firewall delete rule name=\"RGTools-DNS-Guardian-Block-DoT\"").Wait();

            foreach (var server in KnownDohDotServers)
            {
                RunProcessAsync("cmd.exe", $"/c netsh advfirewall firewall delete rule name=\"RGTools-DNS-Guardian-Block-DoH-{server.Replace(".", "-")}\"").Wait();
            }

            _firewallRulesApplied = false;
            LogService.Log("[Guardian] Strict Mode DISABLED - Firewall rules removed.");
        }
        catch (Exception ex)
        {
            LogService.Log($"[Guardian] Firewall Cleanup Error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Stop();
        _lock.Dispose();
    }
}
