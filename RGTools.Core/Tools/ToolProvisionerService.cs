namespace RGTools.App.Core;

public sealed class ToolProvisionerService : IToolProvisioner
{
    private readonly IToolRunner _runner;

    public ToolProvisionerService(IToolRunner runner) => _runner = runner;

    public async Task<ProvisionState> DetectAsync(ToolDescriptor tool, CancellationToken ct = default)
    {
        if (!tool.IsCloned) return ProvisionState.NotCloned;
        if (!tool.IsValid) return ProvisionState.Broken;

        var manifest = tool.Manifest!;
        if (string.IsNullOrWhiteSpace(manifest.Preflight)) return ProvisionState.Ready;

        var result = await _runner.RunAsync(manifest.Preflight, tool.RepoPath!, ct).ConfigureAwait(false);
        return result.Success ? ProvisionState.Ready : ProvisionState.NotReady;
    }

    public async Task<ToolRunResult> EnsureAsync(ToolDescriptor tool, CancellationToken ct = default)
    {
        if (!tool.IsCloned || !tool.IsValid)
            return new ToolRunResult(-1, "La herramienta no está clonada o su manifiesto no es válido.");

        var provision = tool.Manifest!.Provision;
        if (provision.Strategy == ProvisionStrategy.None || string.IsNullOrWhiteSpace(provision.Command))
            return new ToolRunResult(0, "");

        return await _runner.RunAsync(provision.Command, tool.RepoPath!, ct).ConfigureAwait(false);
    }
}
