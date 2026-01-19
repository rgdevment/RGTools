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
    if (!File.Exists(ConfigPath))
    {
      Current = new AppSettings();
      return;
    }

    try
    {
      using var stream = File.OpenRead(ConfigPath);
      Current = await JsonSerializer.DeserializeAsync(stream, AppJsonContext.Default.AppSettings)
                ?? new AppSettings();
    }
    catch
    {
      Current = new AppSettings();
    }
  }

  public async Task SaveAsync(AppSettings newSettings)
  {
    Current = newSettings;
    using var stream = File.Create(ConfigPath);
    await JsonSerializer.SerializeAsync(stream, Current, AppJsonContext.Default.AppSettings);
  }
}

[JsonSerializable(typeof(AppSettings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class AppJsonContext : JsonSerializerContext
{
}
