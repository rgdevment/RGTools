using System.Diagnostics;

namespace RGTools.App.Core;

public sealed class ToolRunner : IToolRunner
{
    public async Task<ToolRunResult> RunAsync(string commandLine, string workingDirectory, CancellationToken ct = default)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {commandLine}",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (process == null)
                return new ToolRunResult(-1, "No se pudo iniciar cmd.exe.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            string output = ((await stdoutTask.ConfigureAwait(false)) + (await stderrTask.ConfigureAwait(false))).Trim();
            int code = process.ExitCode;

            if (code != 0)
                LogService.Log($"[TOOL] '{Shorten(commandLine)}' (cwd={workingDirectory}) -> exit {code}{Environment.NewLine}{output}");
            else
                LogService.Log($"[TOOL] '{Shorten(commandLine)}' (cwd={workingDirectory}) -> exit 0");

            return new ToolRunResult(code, output);
        }
        catch (Exception ex)
        {
            LogService.Log($"[TOOL] Run failed: {Shorten(commandLine)}", ex);
            return new ToolRunResult(-1, ex.Message);
        }
    }

    public bool Launch(string commandLine, string workingDirectory)
    {
        try
        {
            // Visible console so interactive CLI/TUI tools (Rich menus) are usable.
            // TODO(security): child inherits the host's admin token; de-elevate before promoting beyond pilot.
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {commandLine}",
                WorkingDirectory = workingDirectory,
                UseShellExecute = true,
                CreateNoWindow = false
            });
            return process != null;
        }
        catch (Exception ex)
        {
            LogService.Log($"[TOOL] Launch failed: {Shorten(commandLine)}", ex);
            return false;
        }
    }

    private static string Shorten(string value) => value.Length > 80 ? value[..80] + "…" : value;
}
