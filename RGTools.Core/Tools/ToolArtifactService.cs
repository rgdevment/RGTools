using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace RGTools.App.Core;

public sealed class ToolArtifactService : IToolArtifacts
{
    public IReadOnlyList<ArtifactGroup> List(ToolDescriptor tool)
    {
        var groups = new List<ArtifactGroup>();
        if (tool.Manifest?.Artifacts is not { Length: > 0 } specs)
            return groups;

        foreach (var spec in specs)
            groups.Add(new ArtifactGroup(spec.Label, ResolveFiles(spec, tool.RepoPath)));

        return groups;
    }

    public bool Open(string fullPath)
    {
        try
        {
            if (!File.Exists(fullPath)) return false;
            Process.Start(new ProcessStartInfo { FileName = fullPath, UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            LogService.Log($"[TOOL] Failed to open artifact {fullPath}", ex);
            return false;
        }
    }

    private static IReadOnlyList<ArtifactFile> ResolveFiles(ToolArtifact spec, string? repoPath)
    {
        string? dir = ResolveDir(spec.Path, repoPath);
        if (dir == null || !Directory.Exists(dir))
            return Array.Empty<ArtifactFile>();

        try
        {
            bool recursive = spec.Pattern.StartsWith("**/") || spec.Pattern.StartsWith("**\\");
            string filePattern = recursive ? spec.Pattern[3..] : spec.Pattern;
            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            var files = Directory.EnumerateFiles(dir, filePattern, option)
                .Select(p => new ArtifactFile(Path.GetFileNameWithoutExtension(p), p, File.GetLastWriteTime(p)))
                .OrderByDescending(f => f.Modified);

            return (spec.Limit > 0 ? files.Take(spec.Limit) : files).ToList();
        }
        catch (Exception ex)
        {
            LogService.Log($"[TOOL] Artifact listing failed for '{spec.Label}' in {dir}", ex);
            return Array.Empty<ArtifactFile>();
        }
    }

    private static string? ResolveDir(string path, string? repoPath)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        string expanded = Environment.ExpandEnvironmentVariables(path);
        if (Path.IsPathRooted(expanded)) return expanded;

        return repoPath == null ? null : Path.Combine(repoPath, expanded);
    }
}
