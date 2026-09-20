using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace mdaiAgent;

public sealed class AiActionRequest
{
    public string ActionTitle { get; init; } = string.Empty;
    public string UserRequest { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public string FileContent { get; init; } = string.Empty;
    public bool UpdateFile { get; init; }
    public string TargetFilePath { get; init; } = string.Empty;
    public string SelectedFolder { get; init; } = string.Empty;
    public string SystemPrompt { get; init; } = string.Empty;
}

public sealed class AiActionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string AssistantContent { get; set; } = string.Empty;
    public bool UpdatedFile { get; set; }
    public bool HasToolCalls { get; set; }
    public bool ToolCallFailure { get; set; }
    public List<string> UpdatedFilePaths { get; set; } = new();
    public bool RequiresConfirmation { get; set; }
    public string ProposedContent { get; set; } = string.Empty;
    public string OriginalContent { get; set; } = string.Empty;
    public string ProposedFilePath { get; set; } = string.Empty;
}

public class AiActionService
{
    private readonly IAiProvider _apiClient;
    private readonly ToolExecutor _toolExecutor;
    private readonly Action<string> _terminalLog;
    private readonly Action<string, NotificationSeverity>? _notify;
    private readonly Action<string>? _updateOperationStep;

    public AiActionService(
        IAiProvider apiClient,
        ToolExecutor toolExecutor,
        Action<string> terminalLog,
        Action<string, NotificationSeverity>? notify = null,
        Action<string>? updateOperationStep = null)
    {
        _apiClient = apiClient;
        _toolExecutor = toolExecutor;
        _terminalLog = terminalLog;
        _notify = notify;
        _updateOperationStep = updateOperationStep;
    }

    public async Task<AiActionResult> RunFileActionAsync(AiActionRequest request)
    {
        _terminalLog($"AI eylemi başlatıldı: {request.ActionTitle}");
        _updateOperationStep?.Invoke($"AI eylemi başlatıldı: {request.ActionTitle}");

        var messagesToSend = BuildMessages(request);
        var response = await _apiClient.SendChatWithToolsAsync(messagesToSend, ToolRegistry.GetTools());
        var assistantMessage = response?.Choices?.FirstOrDefault()?.Message;

        if (assistantMessage == null)
        {
            return new AiActionResult
            {
                Success = false,
                Message = "AI yanıtı alınamadı."
            };
        }

        if (assistantMessage.ToolCalls != null && assistantMessage.ToolCalls.Count > 0)
        {
            var anyFailure = false;
            var updatedFilePaths = new List<string>();

            foreach (var toolCall in assistantMessage.ToolCalls)
            {
                var toolName = toolCall.Function.Name;
                var toolResultCall = await _toolExecutor.ExecuteAsync(toolName, toolCall.Function.Arguments);
                if (toolResultCall.Success)
                {
                    _terminalLog($"AI araç çağrısı başarıyla tamamlandı: {toolName}");
                    if (toolName == "CreateOrUpdateFile")
                    {
                        var updatedPath = ExtractPathFromToolOutput(toolResultCall.Output);
                        if (!string.IsNullOrEmpty(updatedPath))
                        {
                            updatedFilePaths.Add(updatedPath);
                        }
                    }
                }
                else
                {
                    anyFailure = true;
                    _terminalLog($"AI araç çağrısı hatası: {toolResultCall.Error}");
                    _notify?.Invoke($"AI araç çağrısı hatası: {toolName}", NotificationSeverity.Error);
                }
            }

            return new AiActionResult
            {
                Success = !anyFailure,
                Message = anyFailure ? "Bazı AI araç çağrıları başarısız oldu." : "AI araç çağrıları başarıyla tamamlandı.",
                AssistantContent = assistantMessage.Content ?? string.Empty,
                HasToolCalls = true,
                ToolCallFailure = anyFailure,
                UpdatedFile = updatedFilePaths.Count > 0,
                UpdatedFilePaths = updatedFilePaths
            };
        }

        if (!request.UpdateFile)
        {
            _terminalLog($"AI açıklaması tamamlandı: {request.ActionTitle}");
            _notify?.Invoke($"AI açıklaması tamamlandı: {request.ActionTitle}", NotificationSeverity.Info);

            return new AiActionResult
            {
                Success = true,
                Message = "AI açıklaması tamamlandı.",
                AssistantContent = assistantMessage.Content ?? string.Empty,
                UpdatedFile = false
            };
        }

        var newContent = ExtractCodeFromAiResponse(assistantMessage.Content ?? string.Empty);
        if (string.IsNullOrWhiteSpace(newContent))
        {
            return new AiActionResult
            {
                Success = false,
                Message = "AI kod içeriği çıkarılamadı.",
                AssistantContent = assistantMessage.Content ?? string.Empty
            };
        }

        _terminalLog($"AI önerisi hazırlandı: {request.ActionTitle}");
        _notify?.Invoke($"AI önerisi hazırlandı: {request.ActionTitle}", NotificationSeverity.Info);

        return new AiActionResult
        {
            Success = true,
            Message = "AI önerisi hazırlandı. Değişiklikleri inceleyip onaylayabilirsiniz.",
            AssistantContent = assistantMessage.Content ?? string.Empty,
            UpdatedFile = false,
            RequiresConfirmation = true,
            ProposedContent = newContent,
            OriginalContent = request.FileContent,
            ProposedFilePath = request.TargetFilePath
        };
    }

    private List<ExtendedChatMessage> BuildMessages(AiActionRequest request)
    {
        var systemBuilder = new StringBuilder();

        var baseSystemPrompt = !string.IsNullOrEmpty(request.SystemPrompt)
            ? request.SystemPrompt
            : ToolRegistry.GetSystemPrompt();

        if (!string.IsNullOrWhiteSpace(baseSystemPrompt))
        {
            systemBuilder.AppendLine(baseSystemPrompt.Trim());
        }

        if (!string.IsNullOrEmpty(request.SelectedFolder))
        {
            systemBuilder.AppendLine();
            systemBuilder.AppendLine("[Aktif Proje Dizini]");
            systemBuilder.AppendLine($"Yol: {request.SelectedFolder}");
            systemBuilder.AppendLine("Bu dizin altındaki tüm dosyaları tarayıp ilgili dosyaları bulabilirsin. Açık olmayan dosyaları da bulmak için FindFiles ve SearchCode araçlarını kullan. İhtiyaç olduğunda proje genelinde arama yapıp birden fazla dosyayı düzenleyebilirsin.");
        }

        if (!string.IsNullOrEmpty(request.FilePath) || !string.IsNullOrEmpty(request.FileContent))
        {
            systemBuilder.AppendLine();
            systemBuilder.AppendLine("[Aktif Dosya]");
            systemBuilder.AppendLine($"Dosya: {Path.GetFileName(request.FilePath)}");
            systemBuilder.AppendLine($"Yol: {request.FilePath}");
            systemBuilder.AppendLine("İçerik:");
            systemBuilder.AppendLine("```");
            systemBuilder.AppendLine(request.FileContent ?? string.Empty);
            systemBuilder.AppendLine("```");
        }

        var messages = new List<ExtendedChatMessage>
        {
            new ExtendedChatMessage
            {
                Role = "system",
                Content = systemBuilder.ToString().Trim()
            }
        };

        messages.Add(new ExtendedChatMessage
        {
            Role = "user",
            Content = request.UserRequest
        });

        return messages;
    }

    private string ExtractCodeFromAiResponse(string content)
    {
        var codeBlockPattern = @"```(?:[a-zA-Z0-9_+-]*)\r?\n(?<code>[\s\S]*?)```";
        var match = Regex.Match(content, codeBlockPattern);
        if (match.Success)
        {
            return match.Groups["code"].Value.Trim();
        }

        return content.Trim();
    }

    private string ExtractPathFromToolOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return string.Empty;

        var firstLine = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrEmpty(firstLine))
            return string.Empty;

        const string marker = "SAVED_PATH:";
        if (firstLine.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
        {
            return firstLine.Substring(marker.Length).Trim();
        }

        return string.Empty;
    }
}
