using System;
using System.Threading.Tasks;
using System.Text.Json;
using Xunit;

namespace mdaiAgent.Tests;

public class TerminalCommandRiskTests
{
    [Theory]
    [InlineData("git status", TerminalCommandRiskLevel.Low)]
    [InlineData("dotnet build", TerminalCommandRiskLevel.Low)]
    [InlineData("npm install", TerminalCommandRiskLevel.Medium)]
    [InlineData("git push origin main", TerminalCommandRiskLevel.High)]
    [InlineData("git reset --hard HEAD", TerminalCommandRiskLevel.Critical)]
    public void Analyzer_ClassifiesCommands(string command, TerminalCommandRiskLevel expected)
    {
        var assessment = TerminalCommandRiskAnalyzer.Analyze(command);

        Assert.Equal(expected, assessment.Level);
        Assert.False(string.IsNullOrWhiteSpace(assessment.Reason));
    }

    [Fact]
    public async Task CriticalCommand_IsBlockedBeforeConfirmation()
    {
        var confirmationCount = 0;
        var executor = new ToolExecutor(
            Environment.CurrentDirectory,
            _ => { },
            async _ =>
            {
                confirmationCount++;
                return ToolExecutor.ConfirmResult.Allow;
            },
            async (_, _, _) => true);

        var result = await executor.ExecuteAsync(
            "ExecuteTerminalCommand",
            JsonSerializer.Serialize(new { command = "git reset --hard HEAD" }));

        Assert.False(result.Success);
        Assert.Contains("kritik risk", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, confirmationCount);
    }

    [Fact]
    public async Task HighRiskCommand_RequiresConfirmationOutsideSafeMode()
    {
        var confirmationCount = 0;
        var executor = new ToolExecutor(
            Environment.CurrentDirectory,
            _ => { },
            async _ =>
            {
                confirmationCount++;
                return ToolExecutor.ConfirmResult.Cancel;
            },
            async (_, _, _) => true);

        var result = await executor.ExecuteAsync(
            "ExecuteTerminalCommand",
            JsonSerializer.Serialize(new { command = "git push origin main" }));

        Assert.False(result.Success);
        Assert.Equal(1, confirmationCount);
    }
}
