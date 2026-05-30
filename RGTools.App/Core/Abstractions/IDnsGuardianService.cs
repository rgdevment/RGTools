namespace RGTools.App.Core;

public interface IDnsGuardianService : IDisposable
{
    bool IsRunning { get; }

    event Action<bool>? StatusChanged;

    void Start();

    void Stop();
}
