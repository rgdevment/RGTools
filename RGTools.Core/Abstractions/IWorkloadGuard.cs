namespace RGTools.App.Core;

public record WorkloadSnapshot
{
    public bool WSearchWasRunning { get; init; }
    public bool DockerWasRunning { get; init; }
    public bool LmStudioWasRunning { get; init; }
}

public interface IWorkloadGuard
{
    Task<WorkloadSnapshot> SuspendAsync(CancellationToken ct = default);

    Task RestoreAsync(WorkloadSnapshot snapshot, CancellationToken ct = default);
}
