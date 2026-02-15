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

    private static readonly string? TargetDohTemplate = Environment.GetEnvironmentVariable("PERSONAL_DOH", EnvironmentVariableTarget.Machine);

    private CancellationTokenSource? _cts;
    private ManagementEventWatcher? _networkWatcher;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public bool IsRunning => _cts != null;
    public event Action<bool>? StatusChanged;

    public void Start()
    {
        try
        {
            LogService.Log("[Guardian] Start() called.");
            if (_cts != null)
            {
                LogService.Log("[Guardian] Already running, ignoring Start().");
                return;
            }

            _cts = new CancellationTokenSource();
            LogService.Log("[Guardian] CancellationTokenSource created.");

            StartWmiListener();
            LogService.Log("[Guardian] Starting background loop task...");
            Task.Run(() => LoopAsync(_cts.Token));

            StatusChanged?.Invoke(true);
            LogService.Log("[Guardian] Service Started successfully.");
        }
        catch (Exception ex)
        {
            LogService.LogCrash("[Guardian] CRITICAL: Start() failed", ex);
            throw;
        }
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

        StatusChanged?.Invoke(false);
        LogService.Log("[Guardian] Service Stopped.");
    }

    private void StartWmiListener()
    {
        try
        {
            LogService.Log("[Guardian] Creating WMI query for network changes...");
            var query = new WqlEventQuery("__InstanceModificationEvent",
                TimeSpan.FromSeconds(2),
                "TargetInstance ISA 'Win32_NetworkAdapterConfiguration'");

            _networkWatcher = new ManagementEventWatcher(query);
            _networkWatcher.EventArrived += async (s, e) =>
            {
                LogService.Log("[Guardian] WMI network change event detected.");
                await Task.Delay(2000);
                await CheckAndRestoreDnsAsync("WmiEvent");
            };
            _networkWatcher.Start();
            LogService.Log("[Guardian] WMI listener started successfully.");
        }
        catch (Exception ex)
        {
            LogService.Log($"[Guardian] WMI listener failed to start", ex);
        }
    }

    private async Task LoopAsync(CancellationToken token)
    {
        try
        {
            LogService.Log("[Guardian] Loop started, performing startup DNS check...");
            await CheckAndRestoreDnsAsync("Startup");

            LogService.Log($"[Guardian] Creating timer for {CheckIntervalMinutes} minute intervals...");
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(CheckIntervalMinutes));
            LogService.Log("[Guardian] Entering main monitoring loop.");

            while (await timer.WaitForNextTickAsync(token))
            {
                LogService.Log("[Guardian] Timer tick - running scheduled DNS check.");
                await CheckAndRestoreDnsAsync("Timer");
            }
        }
        catch (OperationCanceledException)
        {
            LogService.Log("[Guardian] Loop cancelled normally (service stopped).");
        }
        catch (Exception ex)
        {
            LogService.LogCrash("[Guardian] CRITICAL: LoopAsync crashed", ex);
            throw;
        }
    }

    private async Task CheckAndRestoreDnsAsync(string source)
    {
        LogService.Log($"[Guardian] ({source}) Check initiated.");

        if (!await _lock.WaitAsync(0))
        {
            LogService.Log($"[Guardian] ({source}) Lock busy, skipping check.");
            return;
        }

        try
        {
            var nic = GetPhysicalInterface();
            if (nic == null)
            {
                LogService.Log($"[Guardian] ({source}) No active physical interface found.");
                return;
            }

            LogService.Log($"[Guardian] ({source}) Checking interface: {nic.Name} [{nic.Description}]");
            var ipProps = nic.GetIPProperties();
            var dnsAddresses = ipProps.DnsAddresses
                .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork)
                .Select(ip => ip.ToString())
                .ToList();

            var currentDns = dnsAddresses.FirstOrDefault() ?? "None";
            LogService.Log($"[Guardian] ({source}) Current DNS: {currentDns}, Expected: {TargetDns}");

            if (currentDns != TargetDns)
            {
                LogService.Log($"[Guardian] ({source}) HIJACK DETECTED! Restoring {TargetDns}...");
                await RestoreDnsIpAsync(nic.Name);
            }
            else
            {
                LogService.Log($"[Guardian] ({source}) DNS is correct.");
            }
        }
        catch (Exception ex)
        {
            LogService.Log($"[Guardian] ({source}) Check failed", ex);
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
        try
        {
            var allInterfaces = NetworkInterface.GetAllNetworkInterfaces();
            LogService.Log($"[Guardian] Found {allInterfaces.Length} total network interfaces.");

            var selected = allInterfaces
                .Where(ni =>
                {
                    var isUp = ni.OperationalStatus == OperationalStatus.Up;
                    var isPhysical = ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                                     ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
                    var hasGateway = ni.GetIPProperties().GatewayAddresses.Count > 0;
                    var notLoopback = ni.NetworkInterfaceType != NetworkInterfaceType.Loopback;

                    if (isUp && isPhysical)
                    {
                        LogService.Log($"[Guardian]   -> {ni.Name} | Type: {ni.NetworkInterfaceType} | Gateway: {hasGateway}");
                    }

                    return isUp && isPhysical && hasGateway && notLoopback;
                })
                .OrderByDescending(ni => ni.Speed)
                .FirstOrDefault();

            if (selected != null)
            {
                LogService.Log($"[Guardian] Selected: {selected.Name} (Speed: {selected.Speed} bps)");
            }

            return selected;
        }
        catch (Exception ex)
        {
            LogService.Log($"[Guardian] GetPhysicalInterface failed", ex);
            return null;
        }
    }

    private async Task RunProcessAsync(string fileName, string args)
    {
        try
        {
            var truncatedArgs = args.Length > 100 ? args.Substring(0, 100) + "..." : args;
            LogService.Log($"[Guardian] Executing: {fileName} {truncatedArgs}");

            ProcessStartInfo psi = new(fileName, args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using Process? p = Process.Start(psi);
            if (p != null)
            {
                await p.WaitForExitAsync();
                LogService.Log($"[Guardian] Process exited: code {p.ExitCode}");

                if (p.ExitCode != 0)
                {
                    var stderr = await p.StandardError.ReadToEndAsync();
                    if (!string.IsNullOrWhiteSpace(stderr))
                    {
                        LogService.Log($"[Guardian] Process error output: {stderr}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Log($"[Guardian] RunProcess failed: {fileName}", ex);
        }
    }

    public void Dispose()
    {
        Stop();
        _lock.Dispose();
    }
}
