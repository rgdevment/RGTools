using System.IO;
using RGTools.App.Core;
using Xunit;

namespace RGTools.Tests;

public class SystemStateStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SystemStateStore _store;

    public SystemStateStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RGToolsState_" + Guid.NewGuid().ToString("N"));
        _store = new SystemStateStore(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private record Sample(int Number, string Text);

    [Fact]
    public async Task Save_Then_Load_RoundTrips()
    {
        await _store.SaveAsync("snap", new Sample(42, "hello"));

        Assert.True(_store.Exists("snap"));
        var loaded = await _store.LoadAsync<Sample>("snap");
        Assert.NotNull(loaded);
        Assert.Equal(42, loaded!.Number);
        Assert.Equal("hello", loaded.Text);
    }

    [Fact]
    public async Task Load_MissingKey_ReturnsDefault()
    {
        var loaded = await _store.LoadAsync<Sample>("missing");

        Assert.Null(loaded);
    }

    [Fact]
    public async Task Clear_RemovesState()
    {
        await _store.SaveAsync("snap", new Sample(1, "x"));
        Assert.True(_store.Exists("snap"));

        _store.Clear("snap");

        Assert.False(_store.Exists("snap"));
    }
}
