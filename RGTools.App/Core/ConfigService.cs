using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RGTools.App.Core;

public sealed class ConfigService : IConfigService
{
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
        try
        {
            Current = newSettings;
            AppPaths.EnsureCreated();

            await using var stream = new FileStream(AppPaths.ConfigFile, FileMode.Create, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(stream, Current, AppJsonContext.Default.AppSettings);
        }
        catch (Exception ex)
        {
            LogService.Log("[CONFIG] Save error", ex);
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
