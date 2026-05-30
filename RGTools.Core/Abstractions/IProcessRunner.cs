namespace RGTools.App.Core;

public interface IProcessRunner
{
    Task<int> RunAsync(string fileName, string arguments, CancellationToken ct = default);

    Task RunPowerShellAsync(string script, CancellationToken ct = default);

    Task<string> RunPowerShellCaptureAsync(string script, CancellationToken ct = default);
}
