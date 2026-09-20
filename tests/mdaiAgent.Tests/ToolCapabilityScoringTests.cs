using System.Linq;
using Xunit;

namespace mdaiAgent.Tests;

public class ToolCapabilityScoringTests
{
    [Fact]
    public void Metadata_ReflectsHigherRiskForFileWritesAndTerminal()
    {
        Assert.True(ToolCapabilityScoring.Get("CreateOrUpdateFile").Risk > ToolCapabilityScoring.Get("ReadFile").Risk);
        Assert.True(ToolCapabilityScoring.Get("ExecuteTerminalCommand").Risk > ToolCapabilityScoring.Get("SearchCode").Risk);
    }

    [Fact]
    public void UnknownTool_UsesConservativeDefaultMetadata()
    {
        var metadata = ToolCapabilityScoring.Get("UnknownTool");

        Assert.Equal(12, metadata.Score);
    }

    [Fact]
    public void OrderByScore_PrioritizesLowerCostTools()
    {
        var tools = ToolRegistry.GetTools()
            .Where(tool => tool.Function.Name is "ExecuteTerminalCommand" or "ReadFile" or "SearchCode")
            .Reverse()
            .ToList();

        var ordered = ToolCapabilityScoring.OrderByScore(tools).ToList();

        Assert.Equal("ReadFile", ordered[0].Function.Name);
        Assert.Equal("ExecuteTerminalCommand", ordered[^1].Function.Name);
    }
}
