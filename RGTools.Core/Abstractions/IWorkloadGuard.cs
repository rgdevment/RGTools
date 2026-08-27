namespace RGTools.App.Core;

public record WorkloadSnapshot
{
    public bool DockerServiceWasRunning { get; init; }
}

public interface IWorkloadGuard
{
    Task<WorkloadSnapshot> CaptureAsync(CancellationToken ct = default);

    Task SuspendAsync(CancellationToken ct = default);

    Task RestoreAsync(WorkloadSnapshot? snapshot, CancellationToken ct = default);
}
