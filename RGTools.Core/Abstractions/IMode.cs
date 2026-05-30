namespace RGTools.App.Core;

public interface IMode
{
    ProfileKind Kind { get; }

    Task ActivateAsync(CancellationToken ct = default);

    Task DeactivateAsync(CancellationToken ct = default);
}
