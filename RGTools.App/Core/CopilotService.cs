using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using Microsoft.Win32;

namespace RGTools.App.Core;

public class CopilotService(ConfigService configService)
{
    private const string RepoUrl = "https://github.com/tu-usuario/meet-copilot-main.git";
    private const string MinVersion = "3.10";
    private const string LmStudioChatEndpoint = "http://localhost:1234/v1/chat/completions";

    public async Task LaunchAsync()
    {
        LogService.Log("[COPILOT] Launch sequence started.");
        try
        {
            var lmTask = ValidateLMStudioActive();
            var pyTask = ValidateEnvironment();

            await Task.WhenAll(lmTask, pyTask);

            if (!lmTask.Result)
            {
                if (MessageBox.Show("LM Studio no tiene un modelo cargado. ¿Continuar?", "Check", MessageBoxButton.YesNo) == MessageBoxResult.No)
                    return;
            }

            if (!pyTask.Result) return;

            string? targetPath = configService.Current.CopilotFolderPath;
            bool isNew = false;

            if (string.IsNullOrEmpty(targetPath) || !Directory.Exists(targetPath))
            {
                targetPath = RequestFolderPath();
                if (targetPath == null) return;
                isNew = true;
            }

            if (!Directory.Exists(Path.Combine(targetPath, ".git")))
            {
                var clone = await CloneRepository(targetPath);
                if (!clone.Success) return;
                targetPath = clone.FinalPath;
                isNew = true;
            }

            if (isNew)
            {
                await configService.SaveAsync(configService.Current with { CopilotFolderPath = targetPath });
            }

            await SetupAndRun(targetPath);
        }
        catch (Exception ex)
        {
            LogService.Log("[COPILOT] Fatal error", ex);
            ShowError(ex.Message);
        }
    }

    public List<FileInfo> GetMeetingFiles()
    {
        try
        {
            string? root = configService.Current.CopilotFolderPath;

            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                return [];
            }

            string logsDir = Path.Combine(root, "reuniones_logs");

            if (!Directory.Exists(logsDir))
            {
                return [];
            }

            return new DirectoryInfo(logsDir)
                .GetFiles("*.md")
                .OrderByDescending(f => f.LastWriteTime)
                .ToList();
        }
        catch (Exception ex)
        {
            LogService.Log("[MEETINGS] Error listing files", ex);
            return [];
        }
    }

    public void OpenMeetingFile(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            LogService.Log("[MEETINGS] Failed to open file", ex);
        }
    }

    private async Task<bool> ValidateLMStudioActive()
    {
        try
        {
            if (!Process.GetProcesses().Any(p => p.ProcessName.Contains("LM Studio", StringComparison.OrdinalIgnoreCase)))
                return false;

            using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(1500) };
            var ping = new LmStudioChatRequest("current", [new LmMessage("user", "ping")], 1);
            var res = await client.PostAsJsonAsync(LmStudioChatEndpoint, ping, AppJsonContext.Default.LmStudioChatRequest);
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private async Task<bool> ValidateEnvironment()
    {
        var output = await GetCommandOutputAsync("python", "--version");
        if (string.IsNullOrEmpty(output))
        {
            ShowError("Python no encontrado.");
            return false;
        }
        var v = output.Replace("Python ", "").Trim();
        if (Version.TryParse(v, out var ver) && ver < new Version(3, 10))
        {
            ShowError($"Python {v} es insuficiente.");
            return false;
        }
        return true;
    }

    private async Task<(bool Success, string FinalPath)> CloneRepository(string path)
    {
        string working = Directory.EnumerateFileSystemEntries(path).Any() ? Path.Combine(path, "meet-copilot-app") : path;
        if (!Directory.Exists(working)) Directory.CreateDirectory(working);

        var (ok, err) = await RunCommandWithDetailsAsync("git", $"clone {RepoUrl} .", working, 300, true);
        if (!ok) ShowError($"Error clonando: {err}");
        return (ok, working);
    }

    private async Task SetupAndRun(string path)
    {
        string venv = Path.Combine(path, ".venv");
        string py = Path.Combine(venv, "Scripts", "python.exe");

        if (!File.Exists(py))
        {
            LogService.Log("[COPILOT] Building environment...");
            await RunCommandWithDetailsAsync("python", "-m venv .venv", path, 120);
            await RunCommandWithDetailsAsync(py, "-m pip install -r requirements.txt", path, 600);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = py,
            Arguments = "main_meeting_ai.py",
            WorkingDirectory = path,
            UseShellExecute = true
        })?.Dispose();
    }

    private async Task<(bool Success, string Error)> RunCommandWithDetailsAsync(string cmd, string args, string dir, int sec, bool inter = false)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(sec));
        using var proc = CreateProcess(cmd, args, dir);
        proc.StartInfo.RedirectStandardError = !inter;
        if (inter)
        {
            proc.StartInfo.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "1";
            proc.StartInfo.EnvironmentVariables["GCM_INTERACTIVE"] = "always";
        }
        else
        {
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
        }

        try
        {
            proc.Start();
            string err = inter ? "" : await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync(cts.Token);
            return (proc.ExitCode == 0, err);
        }
        catch { return (false, "Timeout/Exception"); }
    }

    private async Task<string> GetCommandOutputAsync(string cmd, string args)
    {
        try
        {
            using var proc = CreateProcess(cmd, args, "");
            proc.StartInfo.RedirectStandardOutput = true;
            proc.Start();
            var outStr = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return outStr;
        }
        catch { return ""; }
    }

    private string? RequestFolderPath()
    {
        var dialog = new OpenFolderDialog { Title = "Carpeta Copilot" };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private static Process CreateProcess(string c, string a, string d) => new()
    {
        StartInfo = new() { FileName = c, Arguments = a, WorkingDirectory = d, UseShellExecute = false }
    };

    private void ShowError(string m) => MessageBox.Show(m, "Meet Copilot", MessageBoxButton.OK, MessageBoxImage.Warning);
}
