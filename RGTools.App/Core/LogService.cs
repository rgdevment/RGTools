using System.Diagnostics;
using System.IO;

namespace RGTools.App.Core;

public static class LogService
{
    private static readonly string LogDir = Path.Combine(
        AppContext.BaseDirectory,
        "logs");

    private static readonly string LogPath = Path.Combine(LogDir, "rgtools.log");
    private static readonly string BackupPath = Path.Combine(LogDir, "rgtools.bak");
    private static readonly string CrashPath = Path.Combine(LogDir, "crash.log");

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
                    : $"[{timestamp}] {message}{Environment.NewLine}   >>> Error: {ex.Message}{Environment.NewLine}   >>> StackTrace: {ex.StackTrace}{Environment.NewLine}";

                File.AppendAllText(LogPath, logLine);
                Debug.Write(logLine);
            }
            catch (Exception writeEx)
            {
                Debug.WriteLine($"[CRITICAL] LogService write failure: {writeEx.Message}");
            }
        }
    }

    public static void LogCrash(string message, Exception ex)
    {
        lock (_lockObj)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var crashInfo = $"""
                    ================== CRASH REPORT ==================
                    Time: {timestamp}
                    Message: {message}
                    Exception: {ex.GetType().FullName}
                    Message: {ex.Message}
                    StackTrace:
                    {ex.StackTrace}

                    Inner Exception: {ex.InnerException?.Message ?? "None"}
                    Inner StackTrace:
                    {ex.InnerException?.StackTrace ?? "None"}
                    ==================================================

                    """;

                File.AppendAllText(CrashPath, crashInfo);
                File.AppendAllText(LogPath, crashInfo);
                Debug.Write(crashInfo);
            }
            catch { }
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
