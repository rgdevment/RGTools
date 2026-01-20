using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Text;
using System.Net.Sockets;

namespace RGTools.App.Core;

public class VpnService : IDisposable
{
    private readonly PeriodicTimer _timer = new(TimeSpan.FromMilliseconds(500));
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly Lock _stateLock = new();

    private bool _lastProcessState;
    private bool _lastLinkState;
    private bool _lastConnectionState;
    private int _connectivityTickCount = 0;
    private string? _currentVpnIp;
    private readonly string _trashLog = Path.Combine(Path.GetTempPath(), "forti_trash.log");

    public event Action<bool>? StatusChanged;
    public event Action<bool>? ConnectionChanged;

    public bool IsActive => Process.GetProcesses()
        .Any(p => p.ProcessName.Contains("Forti", StringComparison.OrdinalIgnoreCase) ||
                  p.ProcessName.StartsWith("fc", StringComparison.OrdinalIgnoreCase));

    public bool IsConnected
    {
        get { lock (_stateLock) return _lastConnectionState; }
    }

    public string? VpnIpAddress
    {
        get { lock (_stateLock) return _currentVpnIp; }
    }

    public VpnService()
    {
        _lastProcessState = IsActive;
        _lastLinkState = GetVpnLinkStatus();
        _ = MonitorLoopAsync(_cts.Token);
    }

    public async Task ToggleAsync()
    {
        if (!await _semaphore.WaitAsync(0)) return;

        string action = IsActive ? "SHUTDOWN" : "STARTUP";
        LogService.Log($"[VPN] {action} sequence initiated.");

        try
        {
            if (IsActive)
                await RunEncodedPowerShellAsync(GetShutdownScript());
            else
                await RunEncodedPowerShellAsync(GetStartupScript());

            await Task.Delay(2000);

            lock (_stateLock)
            {
                _lastProcessState = IsActive;
                _lastLinkState = GetVpnLinkStatus();
                StatusChanged?.Invoke(_lastProcessState);
            }
        }
        catch (Exception ex)
        {
            LogService.Log($"[VPN] Critical failure during {action}: {ex.Message}");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task MonitorLoopAsync(CancellationToken token)
    {
        try
        {
            while (await _timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                if (_semaphore.CurrentCount == 0) continue;

                bool currentProcessState = IsActive;
                bool currentLinkState = GetVpnLinkStatus();

                UpdateConnectivity(currentProcessState);

                lock (_stateLock)
                {
                    if (currentProcessState != _lastProcessState)
                    {
                        _lastProcessState = currentProcessState;
                        StatusChanged?.Invoke(currentProcessState);
                    }

                    if (currentLinkState && !_lastLinkState)
                    {
                        LogService.Log("[VPN] Tunnel established. Forcing UI wake...");
                        _ = Task.Run(() => RunEncodedPowerShellAsync(GetGuiLaunchScript()), token);
                    }
                    _lastLinkState = currentLinkState;
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void UpdateConnectivity(bool isActive)
    {
        if (!isActive)
        {
            if (_lastConnectionState)
            {
                lock (_stateLock)
                {
                    _lastConnectionState = false;
                    _currentVpnIp = null;
                }
                ConnectionChanged?.Invoke(false);
            }
            _connectivityTickCount = 0;
            return;
        }

        _connectivityTickCount++;
        if (_connectivityTickCount < 10) return;
        _connectivityTickCount = 0;

        string? ip = GetVpnIPv4Address();
        bool isNowConnected = !string.IsNullOrEmpty(ip);

        lock (_stateLock)
        {
            if (isNowConnected == _lastConnectionState) return;
            _lastConnectionState = isNowConnected;
            _currentVpnIp = ip;
        }

        ConnectionChanged?.Invoke(isNowConnected);
    }

    private string? GetVpnIPv4Address()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(ni => (ni.Description.Contains("Fortinet", StringComparison.OrdinalIgnoreCase) ||
                                   ni.Name.Contains("Forti", StringComparison.OrdinalIgnoreCase)) &&
                                  ni.OperationalStatus == OperationalStatus.Up)
            ?.GetIPProperties().UnicastAddresses
            .FirstOrDefault(ua => ua.Address.AddressFamily == AddressFamily.InterNetwork)?
            .Address.ToString();
    }

    private bool GetVpnLinkStatus()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Any(ni => (ni.Description.Contains("Fortinet", StringComparison.OrdinalIgnoreCase) ||
                        ni.Name.Contains("Forti", StringComparison.OrdinalIgnoreCase)) &&
                       ni.OperationalStatus == OperationalStatus.Up &&
                       ni.NetworkInterfaceType != NetworkInterfaceType.Loopback);
    }

    private async Task RunEncodedPowerShellAsync(string plainScript)
    {
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(plainScript));
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand {encoded}",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var p = Process.Start(psi);
            if (p != null)
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                await p.WaitForExitAsync(timeoutCts.Token);
            }
        }
        catch (Exception ex)
        {
            LogService.Log($"[VPN-PS] Execution error: {ex.Message}");
        }
    }

    private string GetStartupScript() => $@"
        $svc = 'FA_Scheduler'; $dir = 'C:\Program Files\Fortinet\FortiClient';
        $exe = Join-Path $dir 'FortiClient.exe'; $log = '{_trashLog}';
        Get-Process -Name '*Forti*', 'fc*' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue;
        & sc.exe config $svc start= demand >$null 2>&1;
        & sc.exe failure $svc reset= 0 actions= '' >$null 2>&1;
        Start-Service -Name $svc -ErrorAction SilentlyContinue;
        Start-Process -FilePath $exe -WorkingDirectory $dir -WindowStyle Normal -RedirectStandardOutput $log;";

    private string GetGuiLaunchScript() => $@"
        $dir = 'C:\Program Files\Fortinet\FortiClient'; $exe = Join-Path $dir 'FortiClient.exe';
        $log = '{_trashLog}';
        if (Test-Path $exe) {{
            Start-Process -FilePath $exe -WorkingDirectory $dir -WindowStyle Normal -RedirectStandardOutput $log;
        }}";

    private string GetShutdownScript() => @"
        $svc = 'FA_Scheduler';
        & sc.exe config $svc start= disabled >$null 2>&1;
        Stop-Service -Name $svc -Force -ErrorAction SilentlyContinue;
        & taskkill /f /fi ""SERVICES eq $svc"" /t >$null 2>&1;
        Get-Process -Name '*Forti*', 'fc*' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue;";

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _timer.Dispose();
        _semaphore.Dispose();
        GC.SuppressFinalize(this);
    }
}
