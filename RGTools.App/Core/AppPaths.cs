using System.IO;

namespace RGTools.App.Core;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RGTools");

    public static string ConfigFile { get; } = Path.Combine(Root, "config.json");

    public static string LogsDir { get; } = Path.Combine(Root, "logs");

    public static string StatesDir { get; } = Path.Combine(Root, "states");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(StatesDir);
    }
}
