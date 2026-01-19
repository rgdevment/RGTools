using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;

namespace RGTools.App.Core;

public class DnsGuardianService
{
  private readonly string _expectedDns = "192.168.50.100";
  private readonly int _checkIntervalMinutes = 10;
  private CancellationTokenSource? _cts;
  private ManagementEventWatcher? _networkWatcher;
  private string? _lastAnomalousDns;

  public void Start()
  {
    if (_cts != null) return;

    _cts = new CancellationTokenSource();

    StartNetworkChangeListener(); //Por eventos

    Task.Run(() => LoopAsync(_cts.Token)); //Solo por respaldo (10 minutos)

    Debug.WriteLine("[Guardian] Started.");
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

    Debug.WriteLine("[Guardian] Stopped.");
  }

  private void StartNetworkChangeListener()
  {
    try
    {
      var query = new WqlEventQuery("__InstanceModificationEvent",
        TimeSpan.FromSeconds(2),
        "TargetInstance ISA 'Win32_NetworkAdapterConfiguration'");

      _networkWatcher = new ManagementEventWatcher(query);
      _networkWatcher.EventArrived += async (s, e) => await CheckDnsAsync();
      _networkWatcher.Start();

      Debug.WriteLine("[Guardian] Network change listener enabled.");
    }
    catch (Exception ex)
    {
      Debug.WriteLine($"[Guardian] Failed to start network listener: {ex.Message}");
    }
  }

  private async Task LoopAsync(CancellationToken token)
  {
    try
    {
      await CheckDnsAsync();

      while (!token.IsCancellationRequested)
      {
        await Task.Delay(TimeSpan.FromMinutes(_checkIntervalMinutes), token);
        await CheckDnsAsync();
      }
    }
    catch (OperationCanceledException)
    {
      Debug.WriteLine("[Guardian] Cancellation requested, exiting loop.");
    }
    catch (Exception ex)
    {
      Debug.WriteLine($"[Guardian] CRITICAL ERROR: {ex.Message}");
    }
  }

  private async Task CheckDnsAsync()
  {
    try
    {
      var primaryDns = await GetPrimaryPhysicalDnsAsync();

      if (string.IsNullOrEmpty(primaryDns))
      {
        Debug.WriteLine("[Guardian] No DNS detected on physical interfaces.");
        return;
      }

      if (primaryDns != _expectedDns)
      {
        _lastAnomalousDns = primaryDns;
        Debug.WriteLine($"[Guardian] ⚠️ DNS CHANGED! Expected: {_expectedDns}, Found: {primaryDns}");
      }
      else
      {
        Debug.WriteLine($"[Guardian] ✓ DNS OK: {primaryDns}");
      }
    }
    catch (Exception ex)
    {
      Debug.WriteLine($"[Guardian] Error checking DNS: {ex.Message}");
    }
  }

  private async Task<string?> GetPrimaryPhysicalDnsAsync()
  {
    return await Task.Run(() =>
    {
      try
      {
        var interfaces = NetworkInterface.GetAllNetworkInterfaces()
          .Where(ni =>
            ni.OperationalStatus == OperationalStatus.Up &&
            ni.NetworkInterfaceType is
              NetworkInterfaceType.Ethernet or
              NetworkInterfaceType.Wireless80211 &&
            !ni.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
            !ni.Description.Contains("VMware", StringComparison.OrdinalIgnoreCase) &&
            !ni.Description.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase) &&
            !ni.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) &&
            !ni.Description.Contains("Docker", StringComparison.OrdinalIgnoreCase) &&
            !ni.Description.Contains("FortiClient", StringComparison.OrdinalIgnoreCase))
          .OrderByDescending(ni => ni.Speed);

        foreach (var iface in interfaces)
        {
          var dnsAddresses = iface.GetIPProperties().DnsAddresses
            .Where(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            .ToList();

          if (dnsAddresses.Any())
          {
            return dnsAddresses.First().ToString();
          }
        }

        return null;
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"[Guardian] Error getting DNS: {ex.Message}");
        return null;
      }
    });
  }

  public string? GetLastAnomalousDns() => _lastAnomalousDns;
}
