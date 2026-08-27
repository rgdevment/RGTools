using System.IO;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Hosting;

namespace RGTools.App.Core;

public sealed class HealthCheckService : BackgroundService
{
    private const long MinFreeDiskGb = 10;

    private readonly IDnsGuardianService _dns;
    private readonly IProfileEngine _profiles;

    public event Action<string>? StatusChanged;

    public HealthCheckService(IDnsGuardianService dns, IProfileEngine profiles)
    {
        _dns = dns;
        _profiles = profiles;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunChecksAsync(stoppingToken).ConfigureAwait(false);

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false)) break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunChecksAsync(CancellationToken token)
    {
        try
        {
            bool dnsOk = _dns.IsRunning;
            long freeGb = GetFreeDiskGb();
            bool diskOk = freeGb < 0 || freeGb >= MinFreeDiskGb;
            bool netOk = await IsInternetReachableAsync(token).ConfigureAwait(false);

            // Reconciles the declared profile against what Windows actually reports. Not corrected
            // automatically: that would fight the user changing the power mode on purpose.
            var profile = ProfileCatalog.For(_profiles.Active);
            var drift = _profiles.IsApplying ? null : _profiles.Inspect();

            string tooltip =
                $"RGTools — {profile.DisplayName}{(drift?.HasDrift == true ? " (desincronizado)" : "")}\n" +
                $"DNS: {(dnsOk ? "OK" : "off")}\n" +
                $"Disco C: {(freeGb < 0 ? "N/D" : freeGb + " GB")}\n" +
                $"Red: {(netOk ? "OK" : "sin conexión")}";

            StatusChanged?.Invoke(tooltip);

            if (!diskOk) LogService.Log($"[HEALTH] Low disk space: {freeGb} GB");
            if (!netOk) LogService.Log("[HEALTH] Internet unreachable (ping 1.1.1.1).");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LogService.Log("[HEALTH] Check failed", ex);
        }
    }

    private static long GetFreeDiskGb()
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
            return drive.AvailableFreeSpace / (1024 * 1024 * 1024);
        }
        catch
        {
            return -1;
        }
    }

    private static async Task<bool> IsInternetReachableAsync(CancellationToken token)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync("1.1.1.1", TimeSpan.FromSeconds(2), cancellationToken: token).ConfigureAwait(false);
            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }
}
