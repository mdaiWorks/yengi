using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace mdaiAgent.Tests;

public class ProjectMemoryTests
{
    [Fact]
    public async Task ReadProjectMemory_ReturnsStoredContext()
    {
        var projDir = Path.Combine(Path.GetTempPath(), "mdai_test_memory_proj");
        if (Directory.Exists(projDir)) Directory.Delete(projDir, true);
        Directory.CreateDirectory(projDir);

        var executor = new ToolExecutor(projDir, _ => { }, async _ => true, async (_, _, _) => true);
        var args = JsonSerializer.Serialize(new { task = "Add feature", projectPath = projDir });
        var planResult = await executor.ExecuteAsync("CreatePlan", args);

        Assert.True(planResult.Success);

        var memoryArgs = JsonSerializer.Serialize(new { projectPath = projDir });
        var memoryResult = await executor.ExecuteAsync("ReadProjectMemory", memoryArgs);

        Assert.True(memoryResult.Success);
        Assert.Contains("lastTask", memoryResult.Output);
        Assert.Contains("Add feature", memoryResult.Output);

        Directory.Delete(projDir, true);
    }

    [Fact]
    public async Task ProjectMemory2_WritesSearchesAndArchivesConventions()
    {
        var projDir = Path.Combine(Path.GetTempPath(), "mdai_test_memory_v2_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projDir);

        try
        {
            var executor = new ToolExecutor(projDir, _ => { }, async _ => true, async (_, _, _) => true);

            var writeResult = await executor.ExecuteAsync(
                "WriteProjectMemory",
                JsonSerializer.Serialize(new { category = "conventions", key = "testing", value = "xUnit kullan" }));
            Assert.True(writeResult.Success);

            var searchResult = await executor.ExecuteAsync(
                "SearchProjectMemory",
                JsonSerializer.Serialize(new { query = "xUnit" }));
            Assert.True(searchResult.Success);
            Assert.Contains("testing", searchResult.Output);

            var archiveResult = await executor.ExecuteAsync(
                "ArchiveProjectMemory",
                JsonSerializer.Serialize(new { category = "conventions", key = "testing" }));
            Assert.True(archiveResult.Success);

            var memoryJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(projDir, ".mdai", "memory.json")));
            Assert.Equal(2, memoryJson.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.True(memoryJson.RootElement.GetProperty("archived").TryGetProperty("conventions:testing", out _));
        }
        finally
        {
            if (Directory.Exists(projDir)) Directory.Delete(projDir, true);
        }
    }
}
