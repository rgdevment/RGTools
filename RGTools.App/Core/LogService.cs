using System.Diagnostics;
using System.IO;

namespace RGTools.App.Core;

public static class LogService
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RGTools",
        "logs",
        "rgtools.log");
    private static readonly object _lockObj = new();
    private static bool _initialized;

    public static void Initialize()
    {
        try
        {
            if (_initialized) return;

            var dir = Path.GetDirectoryName(LogPath)!;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 2_000_000)
            {
                File.Delete(LogPath);
            }

            Trace.Listeners.Clear();
            var fileListener = new TextWriterTraceListener(LogPath, "LogFile");
            Trace.Listeners.Add(fileListener);
            Trace.Listeners.Add(new DefaultTraceListener());
            Trace.AutoFlush = true;

            Log($"=== RGTools Started ===");
            _initialized = true;
        }
        catch (Exception ex)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var logLine = $"[{timestamp}] [LogService] Failed to initialize: {ex.Message}";
                File.AppendAllText(LogPath, logLine + Environment.NewLine);
            }
            catch { }
        }
    }

    public static void Log(string message)
    {
        lock (_lockObj)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var logLine = $"[{timestamp}] {message}";
                Trace.WriteLine(logLine);
            }
            catch { }
        }
    }

    public static void Shutdown()
    {
        try
        {
            Trace.Flush();
        }
        catch { }
    }

    public static string GetLogPath() => LogPath;
}
