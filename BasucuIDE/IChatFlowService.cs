namespace mdaiAgent;

public interface IChatFlowService
{
    Task<ChatFlowResult> SendMessageAsync(
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
        ChatSession? targetSession = null);
}
