namespace RGTools.App.Core;

public interface IVpnService : IDisposable
{
    bool IsActive { get; }

    bool IsConnected { get; }

    string? VpnIpAddress { get; }

    event Action<bool>? StatusChanged;

    event Action<bool>? ConnectionChanged;

    Task ToggleAsync();
}
