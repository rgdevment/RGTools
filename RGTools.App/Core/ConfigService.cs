using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RGTools.App.Core;

/// <summary>
/// Handles atomic I/O operations for application settings using System.Text.Json Source Generators.
/// This approach avoids runtime reflection for maximum performance and AOT compatibility.
/// </summary>
public class ConfigService
{
  private static readonly string ConfigPath = Path.Combine(AppContext.BaseDirectory, "rgtools.config.json");

  // We maintain the current state in memory for fast access.
  public AppSettings Current { get; private set; } = new();

  /// <summary>
  /// Loads configuration from disk asynchronously.
  /// If file is missing or corrupt, returns default settings.
  /// </summary>
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
      var loaded = await JsonSerializer.DeserializeAsync(
          stream,
          AppJsonContext.Default.AppSettings
      );

      Current = loaded ?? new AppSettings();
    }
    catch
    {
      // Fail safe: If JSON is corrupt, start fresh.
      Current = new AppSettings();
    }
  }

  /// <summary>
  /// Saves the current configuration state to disk asynchronously.
  /// </summary>
  public async Task SaveAsync(AppSettings newSettings)
  {
    Current = newSettings;

    using var stream = File.Create(ConfigPath);
    await JsonSerializer.SerializeAsync(
        stream,
        Current,
        AppJsonContext.Default.AppSettings
    );
  }
}

/// <summary>
/// Source Generator Context.
/// .NET compiler will generate the serialization code for AppSettings at compile time.
/// </summary>
[JsonSerializable(typeof(AppSettings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class AppJsonContext : JsonSerializerContext
{
}
