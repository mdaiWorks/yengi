using System;
using System.Threading.Tasks;
using Xunit;

namespace mdaiAgent.Tests;

public class TerminalServiceTests
{
    [Fact]
    public async Task ExecuteCommandAsync_AppendsCommandToHistoryAndTerminalText()
    {
        var service = new TerminalService(() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        await service.ExecuteCommandAsync("echo test");

        Assert.Contains("echo test", service.TerminalText, StringComparison.OrdinalIgnoreCase);
        Assert.False(service.IsBusy);
    }

    [Fact]
    public void Clear_ResetsTerminalText()
    {
        var service = new TerminalService(() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        service.Clear();

        Assert.Equal(string.Empty, service.TerminalText);
    }

    [Fact]
    public void GetPreviousHistory_ReturnsNullWhenNoHistory()
    {
        var service = new TerminalService(() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var result = service.GetPreviousHistory();

        Assert.Null(result);
    }
}
