using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace mdaiAgent.Tests;

public class ToolExecutorProjectSearchTests
{
    [Fact]
    public async Task FindFiles_ReturnsMatchingProjectFiles()
    {
        var projDir = Path.Combine(Path.GetTempPath(), "mdai_test_proj_search");
        if (Directory.Exists(projDir)) Directory.Delete(projDir, true);
        Directory.CreateDirectory(Path.Combine(projDir, "src"));
        Directory.CreateDirectory(Path.Combine(projDir, "docs"));

        File.WriteAllText(Path.Combine(projDir, "src", "Main.cs"), "class Main {}" );
        File.WriteAllText(Path.Combine(projDir, "src", "Helper.cs"), "class Helper {}" );
        File.WriteAllText(Path.Combine(projDir, "docs", "readme.txt"), "notes" );

        var executor = new ToolExecutor(projDir, _ => { }, async _ => true, async (_, _, _) => true);
        var args = JsonSerializer.Serialize(new { rootPath = projDir, pattern = "*.cs" });
        var result = await executor.ExecuteAsync("FindFiles", args);

        Assert.True(result.Success);
        Assert.Contains("Main.cs", result.Output);
        Assert.Contains("Helper.cs", result.Output);
        Assert.DoesNotContain("readme.txt", result.Output);

        Directory.Delete(projDir, true);
    }

    [Fact]
    public async Task SearchCode_ReturnsMatchingSnippetsAcrossFiles()
    {
        var projDir = Path.Combine(Path.GetTempPath(), "mdai_test_proj_search_code");
        if (Directory.Exists(projDir)) Directory.Delete(projDir, true);
        Directory.CreateDirectory(Path.Combine(projDir, "src"));

        File.WriteAllText(Path.Combine(projDir, "src", "Alpha.cs"), "public class Alpha { public void Run() {} }");
        File.WriteAllText(Path.Combine(projDir, "src", "Beta.cs"), "public class Beta { public void Run() {} }");

        var executor = new ToolExecutor(projDir, _ => { }, async _ => true, async (_, _, _) => true);
        var args = JsonSerializer.Serialize(new { rootPath = projDir, query = "public void Run", includePattern = "*.cs", maxResults = 10 });
        var result = await executor.ExecuteAsync("SearchCode", args);

        Assert.True(result.Success);
        Assert.Contains("Alpha.cs", result.Output);
        Assert.Contains("Beta.cs", result.Output);

        Directory.Delete(projDir, true);
    }

    [Fact]
    public async Task ListDirectory_ReturnsEntries()
    {
        var projDir = Path.Combine(Path.GetTempPath(), "mdai_test_proj_listdir");
        if (Directory.Exists(projDir)) Directory.Delete(projDir, true);
        Directory.CreateDirectory(Path.Combine(projDir, "src"));
        File.WriteAllText(Path.Combine(projDir, "README.md"), "hello");

        var executor = new ToolExecutor(projDir, _ => { }, async _ => true, async (_, _, _) => true);
        var args = JsonSerializer.Serialize(new { path = projDir });
        var result = await executor.ExecuteAsync("ListDirectory", args);

        Assert.True(result.Success);
        Assert.Contains("README.md", result.Output);
        Assert.Contains("src", result.Output);

        Directory.Delete(projDir, true);
    }

    [Fact]
    public async Task BuildProject_ReturnsSuccessForExistingProject()
    {
        var repoRoot = FindRepositoryRoot();
        var executor = new ToolExecutor(repoRoot, _ => { }, async _ => true, async (_, _, _) => true);
        var args = JsonSerializer.Serialize(new { projectPath = Path.Combine(repoRoot, "BasucuIDE", "mdaiAgent.csproj") });
        var result = await executor.ExecuteAsync("BuildProject", args);

        Assert.True(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Output));
    }

    [Fact]
    public async Task RunTests_ReturnsFailureForMissingProject()
    {
        var repoRoot = FindRepositoryRoot();
        var executor = new ToolExecutor(repoRoot, _ => { }, async _ => true, async (_, _, _) => true);
        var args = JsonSerializer.Serialize(new { projectPath = Path.Combine(repoRoot, "missing", "fake.csproj") });
        var result = await executor.ExecuteAsync("RunTests", args);

        Assert.False(result.Success);
        Assert.Contains("Dosya bulunamadı", result.Error ?? string.Empty, System.StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "mdaiAgent.sln")) ||
                Directory.Exists(Path.Combine(current.FullName, "BasucuIDE")) ||
                File.Exists(Path.Combine(current.FullName, "UPDATE_LATEST.md")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return AppContext.BaseDirectory;
    }
}
