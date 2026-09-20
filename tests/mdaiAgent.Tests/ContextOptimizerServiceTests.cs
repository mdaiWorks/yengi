using mdaiAgent;
using Xunit;

namespace mdaiAgent.Tests;

public class ContextOptimizerServiceTests
{
    [Fact]
    public void OptimizeTerminalOutput_AddsDiagnosticSummaryWhenOutputIsTrimmed()
    {
        var optimizer = new ContextOptimizerService();
        var lines = new string[80];
        for (var index = 0; index < lines.Length; index++)
        {
            lines[index] = $"build detail {index}";
        }

        lines[20] = "warning CS1001: sample warning";
        lines[60] = "error CS1002: sample error";

        var result = optimizer.OptimizeTerminalOutput(string.Join('\n', lines));

        Assert.Contains("1 hata", result);
        Assert.Contains("1 uyarı", result);
        Assert.Contains("error CS1002", result);
    }
}
