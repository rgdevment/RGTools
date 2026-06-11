using System.IO;
using System.Text.Json;

namespace RGTools.App.Core;

public sealed class ToolRegistryService : IToolRegistry
{
    // Pilot index. Moves to a bundled tools.default.json (EmbeddedResource) when more tools come online.
    private static readonly ToolIndexEntry[] Index =
    {
        new("videomerge", "videomerge", "git@github.com:rgdevment/videomerge.git"),
        new("netmon", "Netmon", "git@github.com:rgdevment/Netmon.git"),
        new("meet-copilot", "meet-copilot", "git@github.com:rgdevment/meet-copilot.git"),
    };

    private static readonly string[] DefaultRoots =
    {
        @"D:\Code\github_personal",
        @"C:\Code\github_personal",
        @"E:\Code\github_personal",
    };

    private readonly IConfigService _config;
    private List<ToolDescriptor> _all = new();

    public ToolRegistryService(IConfigService config) => _config = config;

    public IReadOnlyList<ToolDescriptor> All => _all;

    public ToolDescriptor? Find(string id) => _all.FirstOrDefault(t => t.Id == id);

    public Task ReloadAsync(CancellationToken ct = default)
    {
        var roots = _config.Current.ToolRoots is { Length: > 0 } configured ? configured : DefaultRoots;
        var list = new List<ToolDescriptor>();

        foreach (var entry in Index)
        {
            string? repoPath = roots
                .Select(root => Path.Combine(root, entry.Folder))
                .FirstOrDefault(Directory.Exists);

            // Clone destination if it's not found: first configured root wins.
            string cloneTarget = Path.Combine(roots[0], entry.Folder);

            if (repoPath == null)
                LogService.Log($"[TOOL] '{entry.Id}' not cloned; will offer clone into {cloneTarget}.");

            list.Add(new ToolDescriptor
            {
                Id = entry.Id,
                RepoUrl = entry.RepoUrl,
                RepoPath = repoPath,
                CloneTarget = cloneTarget,
                Manifest = repoPath == null ? null : ReadManifest(repoPath)
            });
        }

        _all = list;
        return Task.CompletedTask;
    }

    public static ToolManifest? Parse(string json)
    {
        var manifest = JsonSerializer.Deserialize(json, ToolsJsonContext.Default.ToolManifest);
        return manifest is { Schema: 1 } ? manifest : null;
    }

    private static ToolManifest? ReadManifest(string repoPath)
    {
        try
        {
            string file = Path.Combine(repoPath, ".rgtool.json");
            if (!File.Exists(file)) return null;

            var manifest = Parse(File.ReadAllText(file));
            if (manifest == null)
                LogService.Log($"[TOOL] Manifest at {file} missing or unsupported schema; tool not standardized.");
            return manifest;
        }
        catch (Exception ex)
        {
            LogService.Log($"[TOOL] Failed to read manifest in {repoPath}", ex);
            return null;
        }
    }
}
