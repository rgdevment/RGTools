namespace RGTools.App.Core;

public interface ISystemStateStore
{
    bool Exists(string key);

    Task SaveAsync<T>(string key, T state);

    Task<T?> LoadAsync<T>(string key);

    void Clear(string key);
}
