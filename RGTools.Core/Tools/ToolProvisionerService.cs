using System.IO;

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

    public async Task<ToolRunResult> AcquireAsync(ToolDescriptor tool, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tool.RepoUrl) || string.IsNullOrWhiteSpace(tool.CloneTarget))
            return new ToolRunResult(-1, "No hay URL de repositorio o ruta de destino configurada.");

        if (Directory.Exists(tool.CloneTarget))
            return new ToolRunResult(0, "");

        string? parent = Directory.GetParent(tool.CloneTarget.TrimEnd('\\', '/'))?.FullName;
        if (string.IsNullOrEmpty(parent))
            return new ToolRunResult(-1, $"Ruta de destino inválida: {tool.CloneTarget}");

        Directory.CreateDirectory(parent);
        return await _runner.RunAsync($"git clone {tool.RepoUrl} \"{tool.CloneTarget}\"", parent, ct).ConfigureAwait(false);
    }

    public async Task<ToolUpdateResult> UpdateAsync(ToolDescriptor tool, CancellationToken ct = default)
    {
        if (!tool.IsCloned) return new ToolUpdateResult(UpdateOutcome.Skipped, "");
        string repo = tool.RepoPath!;

        // Untracked files are ignored on purpose: they never block a fast-forward, and
        // treating them as dirty would leave the tool stuck on its cloned revision forever.
        var dirty = await _runner.RunAsync("git status --porcelain --untracked-files=no", repo, ct).ConfigureAwait(false);
        if (!dirty.Success)
        {
            LogService.Log($"[TOOL] '{tool.Id}': no es un repositorio git utilizable; no se actualiza.");
            return new ToolUpdateResult(UpdateOutcome.Skipped, dirty.Output);
        }
        if (!string.IsNullOrWhiteSpace(dirty.Output))
        {
            LogService.Log($"[TOOL] '{tool.Id}': hay cambios locales sin confirmar; no se actualiza.");
            return new ToolUpdateResult(UpdateOutcome.Skipped, dirty.Output);
        }

        var before = await _runner.RunAsync("git rev-parse HEAD", repo, ct).ConfigureAwait(false);
        // --ff-only: si el historial divergio falla en vez de fusionar, y se lanza la copia local.
        var pull = await _runner.RunAsync("git pull --ff-only", repo, ct).ConfigureAwait(false);
        if (!pull.Success)
        {
            LogService.Log($"[TOOL] '{tool.Id}': git pull --ff-only fallo; se lanza la copia local.");
            return new ToolUpdateResult(UpdateOutcome.Failed, pull.Output);
        }

        var after = await _runner.RunAsync("git rev-parse HEAD", repo, ct).ConfigureAwait(false);
        bool changed = before.Success && after.Success
            && !string.Equals(before.Output.Trim(), after.Output.Trim(), StringComparison.Ordinal);
        if (changed)
            LogService.Log($"[TOOL] '{tool.Id}' actualizado a {after.Output.Trim()}.");

        return new ToolUpdateResult(changed ? UpdateOutcome.Updated : UpdateOutcome.UpToDate, pull.Output);
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
