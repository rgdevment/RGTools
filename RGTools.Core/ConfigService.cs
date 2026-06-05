using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RGTools.App.Core;

public sealed class ConfigService : IConfigService
{
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly string _configFile;

    public ConfigService() : this(AppPaths.ConfigFile) { }

    public ConfigService(string configFile) => _configFile = configFile;

    public AppSettings Current { get; private set; } = new();

    public async Task LoadAsync()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_configFile)!);

            if (!File.Exists(_configFile)) return;

            await using var stream = new FileStream(_configFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            Current = await JsonSerializer.DeserializeAsync(stream, AppJsonContext.Default.AppSettings)
                      ?? new AppSettings();
        }
        catch (Exception ex)
        {
            LogService.Log("[CONFIG] Load error", ex);
            Current = new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings newSettings)
    {
        await _saveLock.WaitAsync();
        try
        {
            await WriteAtomicAsync(newSettings);
            Current = newSettings;
        }
        catch (Exception ex)
        {
            LogService.Log("[CONFIG] Save error", ex);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public async Task UpdateAsync(Func<AppSettings, AppSettings> mutate)
    {
        await _saveLock.WaitAsync();
        try
        {
            var updated = mutate(Current);
            await WriteAtomicAsync(updated);
            Current = updated;
        }
        catch (Exception ex)
        {
            LogService.Log("[CONFIG] Update error", ex);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private async Task WriteAtomicAsync(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_configFile)!);
        string tempPath = _configFile + ".tmp";

        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, settings, AppJsonContext.Default.AppSettings);
            await stream.FlushAsync();
        }

        File.Move(tempPath, _configFile, overwrite: true);
    }
}

[JsonSerializable(typeof(AppSettings))]
[JsonSourceGenerationOptions(WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
internal partial class AppJsonContext : JsonSerializerContext
{
}
