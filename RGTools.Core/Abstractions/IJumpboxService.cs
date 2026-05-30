namespace RGTools.App.Core;

public record JumpboxResult(bool Success, string? Error = null);

public interface IJumpboxService
{
    Task<JumpboxResult> LaunchAsync(string path);
}
