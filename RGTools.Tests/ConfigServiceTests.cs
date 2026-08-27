using System.IO;
using RGTools.App.Core;
using Xunit;

namespace RGTools.Tests;

public class ConfigServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configFile;

    public ConfigServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RGToolsCfg_" + Guid.NewGuid().ToString("N"));
        _configFile = Path.Combine(_tempDir, "config.json");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [Fact]
    public async Task Save_Then_Load_RoundTripsSettings()
    {
        var svc = new ConfigService(_configFile);
        await svc.SaveAsync(new AppSettings
        {
            DnsGuardianEnabled = false,
            StartWithWindows = true,
            ActiveProfile = ProfileKind.Gaming,
            JumboxFolderPath = "/home/test/jumbox"
        });

        var reloaded = new ConfigService(_configFile);
        await reloaded.LoadAsync();

        Assert.False(reloaded.Current.DnsGuardianEnabled);
        Assert.True(reloaded.Current.StartWithWindows);
        Assert.Equal(ProfileKind.Gaming, reloaded.Current.ActiveProfile);
        Assert.Equal("/home/test/jumbox", reloaded.Current.JumboxFolderPath);
    }

    [Fact]
    public async Task Save_UpdatesCurrent()
    {
        var svc = new ConfigService(_configFile);

        await svc.SaveAsync(new AppSettings { ActiveProfile = ProfileKind.Gaming });

        Assert.Equal(ProfileKind.Gaming, svc.Current.ActiveProfile);
    }

    [Fact]
    public async Task Load_WhenNoFile_KeepsDefaults()
    {
        var svc = new ConfigService(_configFile);

        await svc.LoadAsync();

        Assert.True(svc.Current.DnsGuardianEnabled);
        Assert.Equal(ProfileKind.Balanced, svc.Current.ActiveProfile);
    }

    [Fact]
    public async Task Load_WithRetiredProfile_FallsBackWithoutLosingOtherSettings()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(_configFile, """
            {
              "dnsGuardianEnabled": false,
              "startWithWindows": true,
              "activeProfile": "Boost",
              "jumboxFolderPath": "/home/test/jumbox"
            }
            """);

        var svc = new ConfigService(_configFile);
        await svc.LoadAsync();

        Assert.Equal(ProfileKind.Balanced, svc.Current.ActiveProfile);
        Assert.False(svc.Current.DnsGuardianEnabled);
        Assert.True(svc.Current.StartWithWindows);
        Assert.Equal("/home/test/jumbox", svc.Current.JumboxFolderPath);
    }

    [Fact]
    public async Task Update_AppliesMutation_AndPersists()
    {
        var svc = new ConfigService(_configFile);
        await svc.SaveAsync(new AppSettings { ActiveProfile = ProfileKind.Work });

        await svc.UpdateAsync(s => s with { ActiveProfile = ProfileKind.Gaming, StartWithWindows = true });

        Assert.Equal(ProfileKind.Gaming, svc.Current.ActiveProfile);
        Assert.True(svc.Current.StartWithWindows);

        var reloaded = new ConfigService(_configFile);
        await reloaded.LoadAsync();
        Assert.Equal(ProfileKind.Gaming, reloaded.Current.ActiveProfile);
        Assert.True(reloaded.Current.StartWithWindows);
    }

    [Fact]
    public async Task Save_LeavesNoTempFileBehind()
    {
        var svc = new ConfigService(_configFile);

        await svc.SaveAsync(new AppSettings { ActiveProfile = ProfileKind.Gaming });

        Assert.True(File.Exists(_configFile));
        Assert.False(File.Exists(_configFile + ".tmp"));
    }
}
