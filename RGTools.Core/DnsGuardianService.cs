using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace RGTools.App.Core;

public class DnsGuardianService : IDnsGuardianService
{
    private const string TargetDns = "192.168.10.1";
    private const int CheckIntervalMinutes = 5;
    private const bool EnableDohEncryption = false;

    private static readonly string? TargetDohTemplate = Environment.GetEnvironmentVariable("PERSONAL_DOH", EnvironmentVariableTarget.Machine);

    private CancellationTokenSource? _cts;
    private ManagementEventWatcher? _networkWatcher;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Lock _stateLock = new();
    private bool _disposed;

    // Last reported observation. The WMI watcher polls every 2s and the machine carries a dozen
    // virtual adapters, so logging every check buries everything else; only transitions are written.
    // Guarded by _lock, which serialises CheckAndRestoreDnsAsync.
    private string? _reportedDns;
    private string? _reportedInterface;
    private bool _reportedNoInterface;

    public bool IsRunning
    {
        get { lock (_stateLock) return _cts != null; }
    }

    public event Action<bool>? StatusChanged;

    public void Start()
    {
        try
        {
            CancellationToken token;
            lock (_stateLock)
            {
                if (_disposed) return;
                if (_cts != null)
                {
                    LogService.Log("[Guardian] Already running, ignoring Start().");
                    return;
                }

                _cts = new CancellationTokenSource();
                token = _cts.Token;
            }

            StartWmiListener();
            Task.Run(() => LoopAsync(token));

            RaiseStatusChanged(true);
            LogService.Log($"[Guardian] Started (target {TargetDns}, check every {CheckIntervalMinutes} min).");
        }
        catch (Exception ex)
        {
            LogService.LogCrash("[Guardian] CRITICAL: Start() failed", ex);
            throw;
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        ManagementEventWatcher? watcher;

        lock (_stateLock)
        {
            if (_cts == null) return;
            cts = _cts;
            _cts = null;
            watcher = _networkWatcher;
            _networkWatcher = null;
        }

        cts.Cancel();
        cts.Dispose();

        watcher?.Stop();
        watcher?.Dispose();

        RaiseStatusChanged(false);
        LogService.Log("[Guardian] Service Stopped.");
    }

    private void RaiseStatusChanged(bool state)
    {
        try
        {
            StatusChanged?.Invoke(state);
        }
        catch (Exception ex)
        {
            LogService.Log("[Guardian] StatusChanged subscriber threw", ex);
        }
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
                try
                {
                    await Task.Delay(2000).ConfigureAwait(false);
                    await CheckAndRestoreDnsAsync("WmiEvent").ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogService.LogCrash("[Guardian] WMI event handler failed", ex);
                }
            };
            _networkWatcher.Start();
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
            await CheckAndRestoreDnsAsync("Startup").ConfigureAwait(false);

            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(CheckIntervalMinutes));

            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                await CheckAndRestoreDnsAsync("Timer").ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            LogService.Log("[Guardian] Loop cancelled normally (service stopped).");
        }
        catch (Exception ex)
        {
            LogService.LogCrash("[Guardian] CRITICAL: LoopAsync crashed; stopping guardian.", ex);
            Stop();
        }
    }

    private async Task CheckAndRestoreDnsAsync(string source)
    {
        if (_disposed) return;

        try
        {
            if (!await _lock.WaitAsync(0).ConfigureAwait(false)) return;
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            var nic = GetPhysicalInterface();
            if (nic == null)
            {
                if (!_reportedNoInterface)
                {
                    LogService.Log($"[Guardian] ({source}) No active physical interface found.");
                    _reportedNoInterface = true;
                    _reportedDns = null;
                    _reportedInterface = null;
                }
                return;
            }

            _reportedNoInterface = false;

            var currentDns = nic.GetIPProperties().DnsAddresses
                .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork)
                .Select(ip => ip.ToString())
                .FirstOrDefault() ?? "None";

            if (currentDns != TargetDns)
            {
                LogService.Log($"[Guardian] ({source}) HIJACK on {nic.Name}: {currentDns}, expected {TargetDns}. Restoring...");
                await RestoreDnsIpAsync(nic.Name).ConfigureAwait(false);

                // Cleared so the next check reports the recovered state instead of staying silent.
                _reportedDns = null;
                _reportedInterface = nic.Name;
                return;
            }

            if (currentDns != _reportedDns || nic.Name != _reportedInterface)
            {
                LogService.Log($"[Guardian] ({source}) DNS correct on {nic.Name}: {currentDns}.");
                _reportedDns = currentDns;
                _reportedInterface = nic.Name;
            }
        }
        catch (Exception ex)
        {
            LogService.Log($"[Guardian] ({source}) Check failed", ex);
        }
        finally
        {
            try { _lock.Release(); }
            catch (ObjectDisposedException) { }
        }
    }

    private async Task RestoreDnsIpAsync(string interfaceName)
    {
        await RunProcessAsync("netsh", $"interface ip set dns name=\"{interfaceName}\" static {TargetDns} validate=no").ConfigureAwait(false);

        if (EnableDohEncryption && IsValidDohTemplate(TargetDohTemplate))
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

            await RunProcessAsync("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{psScript}\"").ConfigureAwait(false);
            LogService.Log($"[Guardian] DNS + DoH Restore attempted for '{interfaceName}'.");
        }
    }

    // PERSONAL_DOH is a machine env var injected into a PowerShell -Command string.
    // Require a well-formed https URL so a tampered value can't carry script payloads.
    private static bool IsValidDohTemplate(string? template) =>
        Uri.TryCreate(template, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        !template!.Any(c => char.IsControl(c) || c is '\'' or '"' or ';' or '`' or '$');

    private NetworkInterface? GetPhysicalInterface()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni =>
                    ni.OperationalStatus == OperationalStatus.Up
                    && ni.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211
                    && ni.GetIPProperties().GatewayAddresses.Count > 0)
                .OrderByDescending(ni => ni.Speed)
                .FirstOrDefault();
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
                var stdoutTask = p.StandardOutput.ReadToEndAsync();
                var stderrTask = p.StandardError.ReadToEndAsync();
                await p.WaitForExitAsync().ConfigureAwait(false);
                string stderr = await stderrTask.ConfigureAwait(false);
                await stdoutTask.ConfigureAwait(false);

                if (p.ExitCode != 0)
                    LogService.Log($"[Guardian] {fileName} exited {p.ExitCode}. {stderr}".TrimEnd());
            }
        }
        catch (Exception ex)
        {
            LogService.Log($"[Guardian] RunProcess failed: {fileName}", ex);
        }
    }

    public void Dispose()
    {
        lock (_stateLock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        Stop();

        try { _lock.Dispose(); }
        catch (ObjectDisposedException) { }
    }
}
