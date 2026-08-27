using System.IO;
using System.Runtime.CompilerServices;
using RGTools.App.Core;

namespace RGTools.Tests;

internal static class TestEnvironment
{
    // Runs before any test touches Core. Without it LogService appends every mock exception to the
    // user's real %APPDATA%\RGTools\logs\crash.log and poisons any later diagnosis.
    [ModuleInitializer]
    internal static void RedirectAppPaths()
    {
        string root = Path.Combine(Path.GetTempPath(), "RGTools.Tests", Guid.NewGuid().ToString("N"));

        AppPaths.OverrideRoot(root);
        AppPaths.EnsureCreated();

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(root, recursive: true); }
            catch { }
        };
    }
}
