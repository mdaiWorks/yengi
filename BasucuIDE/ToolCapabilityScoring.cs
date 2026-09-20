using System;
using System.Collections.Generic;
using System.Linq;

namespace mdaiAgent;

public sealed record ToolCapabilityMetadata(
    string ToolName,
    int Risk,
    int Cost,
    int Latency,
    int ContextCost)
{
    public int Score => Risk + Cost + Latency + ContextCost;
}

public static class ToolCapabilityScoring
{
    private static readonly IReadOnlyDictionary<string, ToolCapabilityMetadata> Metadata =
        new Dictionary<string, ToolCapabilityMetadata>(StringComparer.OrdinalIgnoreCase)
        {
            ["ReadFile"] = new("ReadFile", 1, 1, 1, 3),
            ["ListDirectory"] = new("ListDirectory", 1, 1, 1, 1),
            ["FindFiles"] = new("FindFiles", 1, 1, 2, 2),
            ["SearchCode"] = new("SearchCode", 1, 1, 3, 3),
            ["CreateOrUpdateFile"] = new("CreateOrUpdateFile", 5, 2, 2, 4),
            ["ReplaceFileContent"] = new("ReplaceFileContent", 5, 2, 2, 3),
            ["ExecuteTerminalCommand"] = new("ExecuteTerminalCommand", 5, 4, 4, 2),
            ["BuildProject"] = new("BuildProject", 3, 3, 5, 1),
            ["RunTests"] = new("RunTests", 2, 3, 5, 1),
            ["CreateCheckpoint"] = new("CreateCheckpoint", 2, 2, 3, 1),
            ["RollbackToCheckpoint"] = new("RollbackToCheckpoint", 5, 2, 3, 1),
            ["WebSearch"] = new("WebSearch", 2, 3, 4, 2),
            ["WebFetch"] = new("WebFetch", 2, 2, 3, 3),
            ["SmartRecovery"] = new("SmartRecovery", 1, 1, 2, 2)
        };

    public static ToolCapabilityMetadata Get(string toolName)
    {
        return Metadata.TryGetValue(toolName, out var metadata)
            ? metadata
            : new ToolCapabilityMetadata(toolName, 3, 3, 3, 3);
    }

    public static string Describe(string toolName)
    {
        var metadata = Get(toolName);
        return $"risk={metadata.Risk}, cost={metadata.Cost}, latency={metadata.Latency}, context={metadata.ContextCost}, score={metadata.Score}";
    }

    public static IEnumerable<ToolDefinition> OrderByScore(IEnumerable<ToolDefinition> tools)
    {
        return tools.OrderBy(tool => Get(tool.Function?.Name ?? string.Empty).Score)
            .ThenBy(tool => tool.Function?.Name, StringComparer.OrdinalIgnoreCase);
    }
}
