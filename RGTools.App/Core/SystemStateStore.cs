using System.IO;
using System.Text.Json;

namespace RGTools.App.Core;

public sealed class SystemStateStore : ISystemStateStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static string PathFor(string key) => Path.Combine(AppPaths.StatesDir, $"{key}.json");

    public bool Exists(string key) => File.Exists(PathFor(key));

    public async Task SaveAsync<T>(string key, T state)
    {
        try
        {
            AppPaths.EnsureCreated();
            await using var stream = new FileStream(PathFor(key), FileMode.Create, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(stream, state, Options);
        }
        catch (Exception ex)
        {
            LogService.Log($"[STATE] Save '{key}' failed", ex);
        }
    }

    public async Task<T?> LoadAsync<T>(string key)
    {
        try
        {
            if (!File.Exists(PathFor(key))) return default;
            await using var stream = new FileStream(PathFor(key), FileMode.Open, FileAccess.Read, FileShare.Read);
            return await JsonSerializer.DeserializeAsync<T>(stream, Options);
        }
        catch (Exception ex)
        {
            LogService.Log($"[STATE] Load '{key}' failed", ex);
            return default;
        }
    }

    public void Clear(string key)
    {
        try
        {
            if (File.Exists(PathFor(key))) File.Delete(PathFor(key));
        }
        catch (Exception ex)
        {
            LogService.Log($"[STATE] Clear '{key}' failed", ex);
        }
    }
}
