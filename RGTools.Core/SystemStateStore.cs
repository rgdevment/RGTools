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
        Directory.CreateDirectory(_statesDir);

        string finalPath = PathFor(key);
        string tempPath = finalPath + ".tmp";

        try
        {
            // WriteThrough flushes the snapshot to the physical disk before the rename,
            // so a power loss can never leave an empty/half-written rollback state.
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write,
                             FileShare.None, bufferSize: 4096, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, state, Options).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
            }

            File.Move(tempPath, finalPath, overwrite: true);
        }
        catch (Exception ex)
        {
            TryDeleteTemp(tempPath);
            LogService.Log($"[STATE] Save '{key}' failed", ex);
            throw;
        }
    }

    private static void TryDeleteTemp(string tempPath)
    {
        try { if (File.Exists(tempPath)) File.Delete(tempPath); }
        catch { /* orphan .tmp cleanup is best-effort */ }
    }

    public async Task<T?> LoadAsync<T>(string key)
    {
        try
        {
            if (!File.Exists(PathFor(key))) return default;
            await using var stream = new FileStream(PathFor(key), FileMode.Open, FileAccess.Read, FileShare.Read);
            return await JsonSerializer.DeserializeAsync<T>(stream, Options).ConfigureAwait(false);
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
