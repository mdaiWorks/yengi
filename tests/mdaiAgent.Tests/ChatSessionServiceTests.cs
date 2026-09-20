using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace mdaiAgent.Tests;

public class ChatSessionServiceTests
{
    [Fact]
    public void LoadSessions_ReturnsEmptyList_WhenNoFileExists()
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempFolder);

        var service = new ChatSessionService(() => tempFolder);
        var sessions = service.LoadSessions();

        Assert.NotNull(sessions);
        Assert.Empty(sessions);

        Directory.Delete(tempFolder, true);
    }

    [Fact]
    public void SaveSessions_PersistsSessions_AndLoadSessionsReturnsThem()
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempFolder);

        var service = new ChatSessionService(() => tempFolder);
        var session = new ChatSession { Name = "Test Sohbet" };
        service.SaveSessions(new[] { session });

        var loaded = service.LoadSessions();

        Assert.Single(loaded);
        Assert.Equal("Test Sohbet", loaded.First().Name);

        Directory.Delete(tempFolder, true);
    }

    [Fact]
    public void CreateNewSession_AddsSessionToExistingList_AndSavesFile()
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempFolder);

        var service = new ChatSessionService(() => tempFolder);
        var sessions = new System.Collections.Generic.List<ChatSession>();

        var newSession = service.CreateNewSession("Yeni Sohbet", sessions);

        Assert.Single(sessions);
        Assert.Equal(newSession, sessions.First());
        Assert.Equal("Yeni Sohbet", newSession.Name);

        var loaded = service.LoadSessions();
        Assert.Single(loaded);
        Assert.Equal("Yeni Sohbet", loaded.First().Name);

        Directory.Delete(tempFolder, true);
    }
}
