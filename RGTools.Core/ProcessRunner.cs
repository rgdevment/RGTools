using System.Diagnostics;
using System.Text;

namespace RGTools.App.Core;

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<int> RunAsync(string fileName, string arguments, CancellationToken ct = default)
    {
        try
        {
            using var process = Start(fileName, arguments, capture: true);
            if (process == null) return -1;

            await process.WaitForExitAsync(ct);
            LogService.Log($"[PROC] {fileName} {Shorten(arguments)} -> exit {process.ExitCode}");
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            LogService.Log($"[PROC] {fileName} failed", ex);
            return -1;
        }
    }

    public async Task RunPowerShellAsync(string script, CancellationToken ct = default)
    {
        try
        {
            using var process = StartPowerShell(script, capture: false);
            if (process == null) return;
            await process.WaitForExitAsync(ct);
        }
        catch (Exception ex)
        {
            LogService.Log("[PROC] PowerShell execution failed", ex);
        }
    }

    public async Task<string> RunPowerShellCaptureAsync(string script, CancellationToken ct = default)
    {
        try
        {
            using var process = StartPowerShell(script, capture: true);
            if (process == null) return string.Empty;

            string output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            return output;
        }
        catch (Exception ex)
        {
            LogService.Log("[PROC] PowerShell capture failed", ex);
            return string.Empty;
        }
    }

    private static Process? Start(string fileName, string arguments, bool capture) =>
        Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = capture,
            RedirectStandardError = capture
        });

    private static Process? StartPowerShell(string script, bool capture)
    {
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand {encoded}",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = capture,
            RedirectStandardError = capture
        });
    }

    private static string Shorten(string value) => value.Length > 60 ? value[..60] + "…" : value;
}
