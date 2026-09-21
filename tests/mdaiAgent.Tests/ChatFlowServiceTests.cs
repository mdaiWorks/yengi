using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace mdaiAgent.Tests;

public class ChatFlowServiceTests
{
    public ChatFlowServiceTests()
    {
        LocalizationManager.Instance.SetLanguage("tr");
    }

    [Fact]
    public void Initialize_CreatesDefaultSession_WhenNoSessionFileExists()
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempFolder);

        var service = new ChatFlowService(
            new ChatSessionService(() => tempFolder),
            _ => { },
            (_, _) => { },
            _ => { });

        service.Initialize();

        Assert.NotEmpty(service.ChatSessions);
        Assert.NotNull(service.ActiveSession);
        Assert.Equal("Yeni Sohbet", service.ActiveSession!.Name);

        Directory.Delete(tempFolder, true);
    }

    [Fact]
    public async Task SendMessageAsync_ReturnsError_WhenApiClientNotConfigured()
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempFolder);

        var service = new ChatFlowService(
            new ChatSessionService(() => tempFolder),
            _ => { },
            (_, _) => { },
            _ => { });

        service.Initialize();

        var result = await service.SendMessageAsync(
            "Merhaba",
            null,
            null,
            tempFolder,
            "Test prompt");

        Assert.False(result.Success);
        Assert.Contains("Lütfen ayarlardan API anahtarınızı ekleyin", result.Messages.First().Content, StringComparison.OrdinalIgnoreCase);

        Directory.Delete(tempFolder, true);
    }

    [Fact]
    public void TimelineItemViewModel_TracksFailedState()
    {
        var item = new TimelineItemViewModel
        {
            Message = "Hata",
            IsCompleted = false,
            IsFailed = true
        };

        Assert.False(item.IsCompleted);
        Assert.True(item.IsFailed);
    }

    [Fact]
    public void TrimHistoryForEdit_RemovesMessagesAfterTargetUserMessage()
    {
        var history = new List<ExtendedChatMessage>
        {
            new() { Role = "system", Content = "system" },
            new() { Role = "user", Content = "ilk istek" },
            new() { Role = "assistant", Content = "ilk cevap" },
            new() { Role = "user", Content = "ikinci istek" },
            new() { Role = "assistant", Content = "ikinci cevap" }
        };

        var trimmed = ChatFlowService.TrimHistoryForEdit(history, 3);

        Assert.Equal(3, trimmed.Count);
        Assert.Equal("system", trimmed[0].Content);
        Assert.Equal("ilk istek", trimmed[1].Content);
        Assert.Equal("ilk cevap", trimmed[2].Content);
    }

    [Theory]
    [InlineData("devam edelim")]
    [InlineData("continue")]
    [InlineData("devam et")]
    [InlineData("işleme devam")]
    public void IsContinuationMessage_RecognizesTaskResumeMessages(string message)
    {
        var method = typeof(ChatFlowService).GetMethod("IsContinuationMessage", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(method);
        var result = (bool)method!.Invoke(null, new object[] { message })!;

        Assert.True(result);
    }

    [Fact]
    public void IsCasualMessage_DoesNotFlagContinuationMessage()
    {
        var continuationMethod = typeof(ChatFlowService).GetMethod("IsContinuationMessage", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        var casualMethod = typeof(ChatFlowService).GetMethod("IsCasualMessage", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(continuationMethod);
        Assert.NotNull(casualMethod);

        var continuationResult = (bool)continuationMethod!.Invoke(null, new object[] { "devam edelim" })!;
        var casualResult = (bool)casualMethod!.Invoke(null, new object[] { "devam edelim" })!;

        Assert.True(continuationResult);
        Assert.False(casualResult);
    }

    [Fact]
    public void ShouldBlockFinalCompletion_RequiresVerificationForProjectTask()
    {
        var method = typeof(ChatFlowService).GetMethod("ShouldBlockFinalCompletion", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(method);

        var result = (bool)method!.Invoke(null, new object[]
        {
            "TaskBoard API oluştur ve test et",
            "E:\\andoridGames\\test",
            true,
            false,
            true,
            true
        })!;

        Assert.True(result);
    }

    [Fact]
    public void RequiresCapabilityConfirmation_RequiresUserApprovalForHighRiskTools()
    {
        var method = typeof(ToolExecutor).GetMethod("RequiresCapabilityConfirmation", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(method);

        var terminal = (bool)method!.Invoke(null, new object[] { "ExecuteTerminalCommand" })!;
        var build = (bool)method.Invoke(null, new object[] { "BuildProject" })!;
        var read = (bool)method.Invoke(null, new object[] { "ReadFile" })!;

        Assert.True(terminal);
        Assert.True(build);
        Assert.False(read);
    }
}
