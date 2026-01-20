using System.Diagnostics;
using System.IO;

namespace RGTools.App.Core;

public static class LogService
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RGTools",
        "logs");

    private static readonly string LogPath = Path.Combine(LogDir, "rgtools.log");
    private static readonly string BackupPath = Path.Combine(LogDir, "rgtools.bak");

    private static readonly Lock _lockObj = new();

    public static void Initialize()
    {
        try
        {
            if (!Directory.Exists(LogDir))
                Directory.CreateDirectory(LogDir);

            RotateLogsIfNeeded();

            Log("=== RGTools Suite Session Started ===");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CRITICAL] LogService failure: {ex.Message}");
        }
    }

    public static void Log(string message, Exception? ex = null)
    {
        lock (_lockObj)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

                string logLine = ex is null
                    ? $"[{timestamp}] {message}{Environment.NewLine}"
                    : $"[{timestamp}] {message}{Environment.NewLine}   >>> Error: {ex.Message}{Environment.NewLine}";

                File.AppendAllText(LogPath, logLine);
                Debug.Write(logLine);
            }
            catch
            {
                // Fail-safe
            }
        }
    }

    private static void RotateLogsIfNeeded()
    {
        try
        {
            var info = new FileInfo(LogPath);
            if (info.Exists && info.Length > 2 * 1024 * 1024)
            {
                if (File.Exists(BackupPath))
                    File.Delete(BackupPath);

                File.Move(LogPath, BackupPath);
            }
        }
        catch { }
    }

    public static string GetLogPath() => LogPath;
}
