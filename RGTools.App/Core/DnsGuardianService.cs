using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace RGTools.App.Core;

public class DnsGuardianService
{
  private const string TargetDns = "192.168.50.100";
  private const int CheckIntervalMinutes = 5;

  private CancellationTokenSource? _cts;
  private ManagementEventWatcher? _networkWatcher;
  private readonly SemaphoreSlim _lock = new(1, 1);

  public void Start()
  {
    if (_cts != null) return;
    _cts = new CancellationTokenSource();

    StartWmiListener();
    Task.Run(() => LoopAsync(_cts.Token));

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

    LogService.Log($"[Guardian] ({source}) Checking DNS settings...");

    try
    {
      var nic = GetPhysicalInterface();
      if (nic == null) return;

      var ipProps = nic.GetIPProperties();
      var dnsAddresses = ipProps.DnsAddresses
          .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork)
          .Select(ip => ip.ToString())
          .ToList();

      string currentDns = dnsAddresses.FirstOrDefault() ?? string.Empty;

      if (currentDns != TargetDns)
      {
        LogService.Log($"[Guardian] ({source}) HIJACK DETECTED! Found: '{currentDns}'. Restoring to {TargetDns}...");

        await RestoreDnsIpAsync(nic.Name);
      }
      else
      {
        // Optional: Log only on debug to avoid noise
        // Debug.WriteLine($"[Guardian] ({source}) Status Green.");
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
    // Force static DNS IP (Native command, efficient and reliable)
    await RunProcessAsync("netsh", $"interface ip set dns name=\"{interfaceName}\" static {TargetDns} validate=no");

    LogService.Log($"[Guardian] DNS restored for interface '{interfaceName}'.");
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
    if (!AdminHelper.IsAdministrator())
    {
      LogService.Log($"[Sim] {fileName} {args}");
      return;
    }

    try
    {
      var psi = new ProcessStartInfo(fileName, args)
      {
        CreateNoWindow = true,
        UseShellExecute = false
      };
      using var p = Process.Start(psi);
      if (p != null) await p.WaitForExitAsync();
    }
    catch (Exception ex)
    {
      LogService.Log($"[Guardian] Process Error: {ex.Message}");
    }
  }
}
