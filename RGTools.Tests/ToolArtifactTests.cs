using System.IO;
using RGTools.App.Core;
using Xunit;

namespace RGTools.Tests;

public class ToolArtifactTests
{
    private static ToolDescriptor Tool(string? repoPath, params ToolArtifact[] artifacts) =>
        new()
        {
            Id = "tool",
            RepoPath = repoPath,
            Manifest = new ToolManifest { Schema = 1, Artifacts = artifacts }
        };

    [Fact]
    public void Parse_ManifestWithArtifacts_MapsSpec()
    {
        string json = """
        {
          "schema": 1, "id": "netmon", "category": "Network",
          "provision": { "strategy": "ManagedEnv", "command": "uv sync" },
          "launch": { "kind": "Interpreter", "command": "uv run x" },
          "artifacts": [ { "label": "Reportes", "path": "%LOCALAPPDATA%\\netmon", "pattern": "report-*.txt", "limit": 6 } ]
        }
        """;

        var m = ToolRegistryService.Parse(json);

        Assert.NotNull(m);
        Assert.Single(m!.Artifacts);
        Assert.Equal("Reportes", m.Artifacts[0].Label);
        Assert.Equal("report-*.txt", m.Artifacts[0].Pattern);
        Assert.Equal(6, m.Artifacts[0].Limit);
    }

    [Fact]
    public void List_NewestFirst_AndRespectsLimit()
    {
        string root = Path.Combine(Path.GetTempPath(), "rgtools_art_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            for (int i = 0; i < 4; i++)
            {
                string f = Path.Combine(root, $"report-{i}.txt");
                File.WriteAllText(f, "x");
                File.SetLastWriteTime(f, new DateTime(2026, 6, 1).AddDays(i));
            }

            var svc = new ToolArtifactService();
            var groups = svc.List(Tool(@"C:\repo", new ToolArtifact { Label = "Reportes", Path = root, Pattern = "report-*.txt", Limit = 2 }));

            Assert.Single(groups);
            Assert.Equal(2, groups[0].Files.Count);
            Assert.Equal("report-3", groups[0].Files[0].Name);
            Assert.Equal("report-2", groups[0].Files[1].Name);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void List_RelativeRecursivePath_ResolvesAgainstRepo()
    {
        string repo = Path.Combine(Path.GetTempPath(), "rgtools_repo_" + Guid.NewGuid().ToString("N"));
        string sub = Path.Combine(repo, "reuniones_logs", "Daily_2026-06-11");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "Daily_MINUTA.md"), "x");
        File.WriteAllText(Path.Combine(sub, "Daily_RAW.txt"), "x");
        try
        {
            var svc = new ToolArtifactService();
            var groups = svc.List(Tool(repo, new ToolArtifact { Label = "Minutas", Path = "reuniones_logs", Pattern = "**/*_MINUTA.md", Limit = 0 }));

            Assert.Single(groups);
            Assert.Single(groups[0].Files);
            Assert.Equal("Daily_MINUTA", groups[0].Files[0].Name);
        }
        finally
        {
            if (Directory.Exists(repo)) Directory.Delete(repo, true);
        }
    }

    [Fact]
    public void List_NoArtifacts_ReturnsEmpty()
    {
        var svc = new ToolArtifactService();
        Assert.Empty(svc.List(Tool(@"C:\repo")));
    }

    [Fact]
    public void List_MissingDirectory_ReturnsEmptyGroup()
    {
        var svc = new ToolArtifactService();
        var groups = svc.List(Tool(@"C:\repo", new ToolArtifact { Label = "Reportes", Path = @"C:\rgtools_nonexistent_xyz", Pattern = "*.txt" }));

        Assert.Single(groups);
        Assert.Empty(groups[0].Files);
    }
}
