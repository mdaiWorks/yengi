using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace mdaiAgent.Tests;

public class ToolExecutorRollbackTests
{
    [Fact]
    public async Task CreateOrUpdateFile_ReadOnlyTarget_FailsAndOriginalRemains()
    {
        var projDir = Path.Combine(Path.GetTempPath(), "mdai_test_proj_rb");
        if (Directory.Exists(projDir))
        {
            try
            {
                foreach (var f in Directory.GetFiles(projDir, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
                }
                Directory.Delete(projDir, true);
            }
            catch { }
        }
        Directory.CreateDirectory(projDir);

        var filePath = Path.Combine(projDir, "test.txt");
        File.WriteAllText(filePath, "original-content");
        // make target read-only to cause copy/overwrite errors
        File.SetAttributes(filePath, FileAttributes.ReadOnly);

        var executor = new ToolExecutor(projDir, s => { }, async (cmd) => true, async (f, o, n) => true);
        var args = System.Text.Json.JsonSerializer.Serialize(new { filePath = filePath, content = "new content" });
        var res = await executor.ExecuteAsync("CreateOrUpdateFile", args);

        // Expect failure due to read-only overwrite
        Assert.False(res.Success);

        // Original content should remain
        var written = File.ReadAllText(filePath);
        Assert.Equal("original-content", written);

        // cleanup - remove readonly attributes recursively then delete
        try
        {
            foreach (var f in Directory.GetFiles(projDir, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
            }
            Directory.Delete(projDir, true);
        }
        catch { }
    }
}
