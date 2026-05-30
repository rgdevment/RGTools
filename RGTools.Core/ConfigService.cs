using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RGTools.App.Core;

public sealed class ConfigService : IConfigService
{
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public AppSettings Current { get; private set; } = new();

    public async Task LoadAsync()
    {
        try
        {
            AppPaths.EnsureCreated();

            if (!File.Exists(AppPaths.ConfigFile)) return;

            await using var stream = new FileStream(AppPaths.ConfigFile, FileMode.Open, FileAccess.Read, FileShare.Read);
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
            AppPaths.EnsureCreated();

            await using (var stream = new FileStream(AppPaths.ConfigFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, newSettings, AppJsonContext.Default.AppSettings);
            }

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
}

[JsonSerializable(typeof(AppSettings))]
[JsonSourceGenerationOptions(WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
internal partial class AppJsonContext : JsonSerializerContext
{
}
