using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace mdaiAgent.Tests;

public class CheckpointRollbackTests
{
    [Fact]
    public async Task CreateCheckpoint_AndRollback_Work()
    {
        var projDir = Path.Combine(Path.GetTempPath(), "mdai_test_checkpoint");
        if (Directory.Exists(projDir)) Directory.Delete(projDir, true);
        Directory.CreateDirectory(projDir);

        var filePath = Path.Combine(projDir, "sample.txt");
        File.WriteAllText(filePath, "before");

        var executor = new ToolExecutor(projDir, _ => { }, async _ => true, async (_, _, _) => true);
        var checkpointArgs = JsonSerializer.Serialize(new { projectPath = projDir, label = "before-edit" });
        var checkpointResult = await executor.ExecuteAsync("CreateCheckpoint", checkpointArgs);

        File.WriteAllText(filePath, "after");

        var rollbackArgs = JsonSerializer.Serialize(new { projectPath = projDir, label = "before-edit" });
        var rollbackResult = await executor.ExecuteAsync("RollbackToCheckpoint", rollbackArgs);

        Assert.True(checkpointResult.Success);
        Assert.True(rollbackResult.Success);
        Assert.Equal("before", File.ReadAllText(filePath));

        Directory.Delete(projDir, true);
    }
}
