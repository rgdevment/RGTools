namespace RGTools.App.Core;

public interface IKillAllService
{
    Task ExecuteAsync(CancellationToken ct = default);
}
