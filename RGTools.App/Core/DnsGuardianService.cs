using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace RGTools.App.Core;

public class DnsGuardianService : IDisposable
{
  private const string TargetDns = "192.168.50.100";
  private const int CheckIntervalMinutes = 5;

  private static readonly string? TargetDohTemplate = Environment.GetEnvironmentVariable("PERSONAL_DOH", EnvironmentVariableTarget.Machine);

  private CancellationTokenSource? _cts;
  private ManagementEventWatcher? _networkWatcher;
  private readonly SemaphoreSlim _lock = new(1, 1);

  public bool IsRunning => _cts != null;
  public event Action<bool>? StatusChanged;

  public void Start()
  {
    if (_cts != null) return;
    _cts = new CancellationTokenSource();

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

    if (!string.IsNullOrEmpty(TargetDohTemplate))
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

  public void Dispose()
  {
    Stop();
    _lock.Dispose();
  }
}
