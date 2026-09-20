using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace mdaiAgent.Tests;

public class PlanModeServiceTests
{
    private class FakeChatFlowService : IChatFlowService
    {
        public Task<ChatFlowResult> SendMessageAsync(
            string message,
            string? currentFilePath,
            string? currentFileContent,
            string? selectedFolder,
            string systemPrompt,
            bool qaAgentEnabled,
            bool uiAgentEnabled,
            List<Attachment>? attachments = null,
            CancellationToken cancellationToken = default,
            Action<string>? onTokenReceived = null,
            Action<ChatFlowMessage>? onMessageAdded = null,
            bool appendUserMessageToHistory = true,
            ChatSession? targetSession = null)
        {
            var responseText = "1. Adım bir\n- İlk değişiklik\n- Sonraki adım\n\n2. Adım iki\n- İkinci değişiklik";
            return Task.FromResult(new ChatFlowResult
            {
                Success = true,
                Messages = new List<ChatFlowMessage>
                {
                    new ChatFlowMessage { Sender = "AI Asistan", Content = responseText }
                }
            });
        }
    }

    [Fact]
    public async Task CreatePlanAsync_ParsesOptionsFromAiResponse()
    {
        var planModeService = new PlanModeService(new FakeChatFlowService());

        var result = await planModeService.CreatePlanAsync("file.cs", "content", "folder", "prompt");

        Assert.True(result.Success);
        Assert.Equal(2, result.Options.Count);
        Assert.Equal("1", result.Options[0].Id);
        Assert.Contains("Adım bir", result.Options[0].Title);
        Assert.Contains("İlk değişiklik", result.Options[0].Description);
    }

    [Fact]
    public async Task CreateFollowUpAsync_ReturnsFollowUpText()
    {
        var planModeService = new PlanModeService(new FakeChatFlowService());
        var selectedOption = new PlanOption
        {
            Id = "1",
            Title = "Adım bir",
            Description = "Detay açıklama"
        };

        var result = await planModeService.CreateFollowUpAsync(selectedOption, "file.cs", "content", "folder", "prompt");

        Assert.True(result.Success);
        Assert.Contains("Adım bir", result.FullPlanText);
    }
}
