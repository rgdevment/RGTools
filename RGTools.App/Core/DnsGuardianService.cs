using System.Diagnostics;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using Microsoft.Win32;

namespace RGTools.App.Core;

public class DnsGuardianService
{
  private readonly string _expectedDns = "192.168.50.100";
  private readonly string _expectedDoH = "https://query";
  private readonly int _checkIntervalMinutes = 5;
  private CancellationTokenSource? _cts;
  private ManagementEventWatcher? _networkWatcher;
  private string? _lastAnomalousDns;

  public void Start()
  {
    if (_cts != null) return;

    _cts = new CancellationTokenSource();

    StartNetworkChangeListener(); //Por eventos

    Task.Run(() => LoopAsync(_cts.Token)); //Solo por respaldo (5 minutos)

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
      var dnsIP = primaryDns?.First().ToString() ?? null;

      var dohTemplate = GetDohTemplateForDns(dnsIP);
      LogService.Log($"[Guardian] DoH Template for DNS {dnsIP}: {dohTemplate}");

      if (string.IsNullOrEmpty(dnsIP))
      {
        Debug.WriteLine("[Guardian] No DNS detected on physical interfaces.");
        return;
      }

      if (dnsIP != _expectedDns)
      {
        _lastAnomalousDns = dnsIP;
        Debug.WriteLine($"[Guardian] ⚠️ DNS CHANGED! Expected: {_expectedDns}, Found: {dnsIP}");
      }
      else
      {
        Debug.WriteLine($"[Guardian] ✓ DNS OK: {dnsIP}");
      }
    }
    catch (Exception ex)
    {
      Debug.WriteLine($"[Guardian] Error checking DNS: {ex.Message}");
    }
  }

  private async Task<List<IPAddress>?> GetPrimaryPhysicalDnsAsync()
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
            return dnsAddresses;
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

  private static string? GetDohTemplateForDns(string? dnsIp)
  {
    if (string.IsNullOrEmpty(dnsIp)) return null;

    try
    {
      const string basePath = @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters\DohWellKnownServers";
      using var key = Registry.LocalMachine.OpenSubKey($@"{basePath}\{dnsIp}");
      return key?.GetValue("Template") as string;
    }
    catch (Exception ex)
    {
      Debug.WriteLine($"[Guardian] Error getting DoH template: {ex.Message}");
      return null;
    }
  }
}
