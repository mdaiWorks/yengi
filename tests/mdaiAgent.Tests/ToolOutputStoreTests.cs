using System.Threading.Tasks;
using mdaiAgent.Services;
using Xunit;

namespace mdaiAgent.Tests;

public class ToolOutputStoreTests
{
    [Fact]
    public async Task SaveIfTruncated_ReturnsIdAndReadsRequestedLines()
    {
        var store = new ToolOutputStore();
        var fullOutput = "line 1\nline 2\nline 3";

        var outputId = store.SaveIfTruncated(fullOutput, "line 1\n... output shortened ...", "test");
        Assert.False(string.IsNullOrWhiteSpace(outputId));

        var result = await store.ReadAsync(outputId!, 2, 3);

        Assert.True(result.Success);
        Assert.Equal("line 2\r\nline 3", result.Output);
    }

    [Fact]
    public void SaveIfTruncated_DoesNotStoreUnchangedOutput()
    {
        var store = new ToolOutputStore();

        var outputId = store.SaveIfTruncated("short output", "short output", "test");

        Assert.Null(outputId);
    }
}
