using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RGTools.App.Core;

public class ConfigService
{
    private static readonly string ConfigPath = Path.Combine(AppContext.BaseDirectory, "rgtools.config.json");

    public AppSettings Current { get; private set; } = new();

    public async Task LoadAsync()
    {
        if (!File.Exists(ConfigPath)) return;

        try
        {
            using var stream = new FileStream(ConfigPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            Current = await JsonSerializer.DeserializeAsync(stream, AppJsonContext.Default.AppSettings)
                      ?? new AppSettings();
        }
        catch (Exception ex)
        {
            LogService.Log($"[CONFIG] Load error: {ex.Message}");
            Current = new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings newSettings)
    {
        try
        {
            Current = newSettings;
            using var stream = new FileStream(ConfigPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(stream, Current, AppJsonContext.Default.AppSettings);
        }
        catch (Exception ex)
        {
            LogService.Log($"[CONFIG] Save error: {ex.Message}");
        }
    }
}

[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(LmStudioResponse))]
[JsonSerializable(typeof(LmStudioChatRequest))]
[JsonSourceGenerationOptions(WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class AppJsonContext : JsonSerializerContext
{
}
