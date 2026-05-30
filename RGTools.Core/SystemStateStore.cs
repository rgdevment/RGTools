using System.IO;
using System.Text.Json;

namespace RGTools.App.Core;

public sealed class SystemStateStore : ISystemStateStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _statesDir;

    public SystemStateStore() : this(AppPaths.StatesDir) { }

    public SystemStateStore(string statesDir) => _statesDir = statesDir;

    private string PathFor(string key) => Path.Combine(_statesDir, $"{key}.json");

    public bool Exists(string key) => File.Exists(PathFor(key));

    public async Task SaveAsync<T>(string key, T state)
    {
        try
        {
            Directory.CreateDirectory(_statesDir);

            string finalPath = PathFor(key);
            string tempPath = finalPath + ".tmp";

            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, state, Options);
                await stream.FlushAsync();
            }

            File.Move(tempPath, finalPath, overwrite: true);
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
