using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace mdaiAgent.Tests;

public class SelfHealingTests
{
    [Fact]
    public async Task RetryPlan_ReturnsRecoverySuggestion()
    {
        var projDir = Path.Combine(Path.GetTempPath(), "mdai_test_selfheal");
        if (Directory.Exists(projDir)) Directory.Delete(projDir, true);
        Directory.CreateDirectory(projDir);

        var executor = new ToolExecutor(projDir, _ => { }, async _ => true, async (_, _, _) => true);
        var args = JsonSerializer.Serialize(new { task = "Fix compile error", projectPath = projDir, lastError = "CS1002 syntax error" });
        var result = await executor.ExecuteAsync("RetryPlan", args);

        Assert.True(result.Success);
        Assert.Contains("recovery", result.Output, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CS1002", result.Output);

        Directory.Delete(projDir, true);
    }
}
