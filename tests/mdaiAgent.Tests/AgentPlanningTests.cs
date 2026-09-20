using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace mdaiAgent.Tests;

public class AgentPlanningTests
{
    [Fact]
    public async Task CreatePlan_ReturnsStructuredPlan()
    {
        var projDir = Path.Combine(Path.GetTempPath(), "mdai_test_plan_proj");
        if (Directory.Exists(projDir)) Directory.Delete(projDir, true);
        Directory.CreateDirectory(projDir);
        File.WriteAllText(Path.Combine(projDir, "Program.cs"), "class Program {}" );

        var executor = new ToolExecutor(projDir, _ => { }, async _ => true, async (_, _, _) => true);
        var args = JsonSerializer.Serialize(new { task = "Add a hello method", projectPath = projDir });
        var result = await executor.ExecuteAsync("CreatePlan", args);

        Assert.True(result.Success);
        Assert.Contains("Plan", result.Output, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Program.cs", result.Output);

        Directory.Delete(projDir, true);
    }

    [Fact]
    public async Task GenerateDiff_ReturnsPatchPreview()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "mdai_test_diff.txt");
        File.WriteAllText(tempFile, "old");

        var executor = new ToolExecutor(Path.GetTempPath(), _ => { }, async _ => true, async (_, _, _) => true);
        var args = JsonSerializer.Serialize(new { filePath = tempFile, oldContent = "old", newContent = "new" });
        var result = await executor.ExecuteAsync("GenerateDiff", args);

        Assert.True(result.Success);
        Assert.Contains("- old", result.Output);
        Assert.Contains("+ new", result.Output);

        File.Delete(tempFile);
    }
}
