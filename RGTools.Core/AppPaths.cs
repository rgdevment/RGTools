using System.IO;

namespace RGTools.App.Core;

public static class AppPaths
{
    private static string _root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RGTools");

    public static string Root => _root;

    // Not a constant so a test run can redirect the whole tree; otherwise xUnit writes into the
    // user's real %APPDATA%\RGTools and its mock exceptions land in the production crash log.
    public static void OverrideRoot(string root) => _root = root;

    public static string ConfigFile => Path.Combine(_root, "config.json");

    public static string LogsDir => Path.Combine(_root, "logs");

    public static string StatesDir => Path.Combine(_root, "states");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(StatesDir);
    }
}
