using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace mdaiAgent;

public class AnthropicApiClient : IAiProvider
{
    private readonly AppSettings _settings;
    private readonly HttpClient _httpClient;

    public string ModelName => _settings.AnthropicModel;

    public AnthropicApiClient(AppSettings settings, HttpClient? httpClient = null)
    {
        _settings = settings;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.ApiTimeoutSeconds));
    }

    public async Task<ExtendedChatResponse?> SendChatWithToolsAsync(
        List<ExtendedChatMessage> messages, 
        List<ToolDefinition> tools, 
        CancellationToken cancellationToken = default)
    {
        var apiKey = _settings.AnthropicApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Anthropic API anahtarı ayarlanmamış.");

        var system = string.Join("\n", messages.Where(m => string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase)).Select(m => m.Content).Where(c => !string.IsNullOrWhiteSpace(c)));
        var requestMessages = messages
            .Where(m => !string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase))
            .Select(m => new { role = m.Role == "assistant" ? "assistant" : "user", content = BuildContent(m) })
            .ToList();

        var request = new Dictionary<string, object?>
        {
            ["model"] = _settings.AnthropicModel,
            ["max_tokens"] = 8192,
            ["messages"] = requestMessages
        };
        if (!string.IsNullOrWhiteSpace(system)) request["system"] = system;
        if (tools.Count > 0)
            request["tools"] = tools.Select(t => new
            {
                name = t.Function.Name,
                description = t.Function.Description,
                input_schema = t.Function.Parameters
            }).ToList();

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _settings.AnthropicBaseUrl.TrimEnd('/') + "/messages")
        {
            Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Add("x-api-key", apiKey.Trim());
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");
        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Anthropic API hatası ({(int)response.StatusCode}): {body}");

        return ParseResponse(body);
    }

    public async Task<ExtendedChatResponse?> SendChatWithToolsStreamAsync(
        List<ExtendedChatMessage> messages, 
        List<ToolDefinition> tools, 
        Action<string>? onTokenReceived = null, 
        CancellationToken cancellationToken = default)
    {
        var apiKey = _settings.AnthropicApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Anthropic API anahtarı ayarlanmamış.");

        var system = string.Join("\n", messages.Where(m => m.Role.Equals("system", StringComparison.OrdinalIgnoreCase)).Select(m => m.Content).Where(c => !string.IsNullOrWhiteSpace(c)));
        var requestMessages = messages.Where(m => !m.Role.Equals("system", StringComparison.OrdinalIgnoreCase))
            .Select(m => new { role = m.Role == "assistant" ? "assistant" : "user", content = BuildContent(m) }).ToList();
        var request = new Dictionary<string, object?>
        {
            ["model"] = _settings.AnthropicModel,
            ["max_tokens"] = 8192,
            ["messages"] = requestMessages,
            ["stream"] = true
        };
        if (!string.IsNullOrWhiteSpace(system)) request["system"] = system;
        if (tools.Count > 0) request["tools"] = tools.Select(t => new { name = t.Function.Name, description = t.Function.Description, input_schema = t.Function.Parameters }).ToList();

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _settings.AnthropicBaseUrl.TrimEnd('/') + "/messages")
        {
            Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Add("x-api-key", apiKey.Trim());
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");
        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Anthropic API hatası ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync(cancellationToken)}");

        var message = new ExtendedChatMessage { Role = "assistant", Content = "" };
        var text = new StringBuilder();
        var activeToolCall = default(ToolCall);
        var toolCalls = new Dictionary<string, ToolCall>();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var json = line[5..].Trim();
            if (json == "[DONE]") break;

            using var eventDocument = JsonDocument.Parse(json);
            var root = eventDocument.RootElement;

            if (root.TryGetProperty("type", out var eventType) && eventType.GetString() == "content_block_start")
            {
                if (root.TryGetProperty("content_block", out var contentBlock) &&
                    contentBlock.TryGetProperty("type", out var blockType) &&
                    blockType.GetString() == "tool_use")
                {
                    var toolId = contentBlock.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString("N");
                    var toolName = contentBlock.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                    var toolCall = new ToolCall
                    {
                        Id = toolId,
                        Function = new FunctionCall
                        {
                            Name = toolName,
                            Arguments = "{}"
                        }
                    };
                    toolCalls[toolId] = toolCall;
                    activeToolCall = toolCall;
                }
                continue;
            }

            if (root.TryGetProperty("delta", out var delta))
            {
                if (delta.TryGetProperty("type", out var deltaType) &&
                    deltaType.GetString() == "text_delta" &&
                    delta.TryGetProperty("text", out var token))
                {
                    var value = token.GetString() ?? "";
                    text.Append(value);
                    onTokenReceived?.Invoke(value);
                    continue;
                }

                if (delta.TryGetProperty("type", out var toolDeltaType) &&
                    toolDeltaType.GetString() == "input_json_delta" &&
                    delta.TryGetProperty("partial_json", out var partialJson))
                {
                    var partialValue = partialJson.GetString() ?? "";
                    if (activeToolCall == null)
                    {
                        continue;
                    }

                    var currentJson = activeToolCall.Function.Arguments;
                    var merged = string.Concat(currentJson, partialValue);
                    if (string.IsNullOrEmpty(currentJson) || currentJson == "{}")
                    {
                        merged = partialValue;
                    }

                    activeToolCall.Function.Arguments = merged;
                    if (toolCalls.ContainsKey(activeToolCall.Id))
                    {
                        toolCalls[activeToolCall.Id] = activeToolCall;
                    }
                }
            }

            if (root.TryGetProperty("type", out var stopType) && stopType.GetString() == "content_block_stop")
            {
                if (activeToolCall != null)
                {
                    activeToolCall = null;
                }
            }
        }

        if (toolCalls.Count > 0)
        {
            message.ToolCalls = toolCalls.Values.ToList();
        }

        message.Content = text.ToString();
        return new ExtendedChatResponse { Choices = new List<ExtendedChatResponseChoice> { new() { Index = 0, Message = message } } };
    }

    private static object BuildContent(ExtendedChatMessage message)
    {
        if (message.ToolCalls != null)
            return message.ToolCalls.Select(call => new
            {
                type = "tool_use",
                id = call.Id,
                name = call.Function.Name,
                input = ParseObject(call.Function.Arguments)
            }).ToList();

        if (!string.IsNullOrEmpty(message.ToolCallId))
            return new[] { new { type = "tool_result", tool_use_id = message.ToolCallId, content = message.Content ?? "" } };

        return message.Content ?? "";
    }

    private static object ParseObject(string json)
    {
        try { return JsonSerializer.Deserialize<JsonElement>(json); }
        catch { return new { value = json }; }
    }

    private static ExtendedChatResponse ParseResponse(string body)
    {
        using var document = JsonDocument.Parse(body);
        var message = new ExtendedChatMessage { Role = "assistant", Content = "" };
        var content = new StringBuilder();
        if (document.RootElement.TryGetProperty("content", out var blocks))
        {
            foreach (var block in blocks.EnumerateArray())
            {
                var type = block.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;
                if (type == "text" && block.TryGetProperty("text", out var text)) content.Append(text.GetString());
                if (type == "tool_use")
                {
                    message.ToolCalls ??= new List<ToolCall>();
                    message.ToolCalls.Add(new ToolCall
                    {
                        Id = block.GetProperty("id").GetString() ?? Guid.NewGuid().ToString("N"),
                        Function = new FunctionCall
                        {
                            Name = block.GetProperty("name").GetString() ?? "",
                            Arguments = block.TryGetProperty("input", out var input) ? input.GetRawText() : "{}"
                        }
                    });
                }
            }
        }
        message.Content = content.ToString();
        var result = new ExtendedChatResponse
        {
            Choices = new List<ExtendedChatResponseChoice> { new() { Index = 0, Message = message } }
        };
        if (document.RootElement.TryGetProperty("usage", out var usage))
            result.Usage = new ChatUsage
            {
                PromptTokens = usage.TryGetProperty("input_tokens", out var input) ? input.GetInt32() : 0,
                CompletionTokens = usage.TryGetProperty("output_tokens", out var output) ? output.GetInt32() : 0
            };
        if (result.Usage != null) result.Usage.TotalTokens = result.Usage.PromptTokens + result.Usage.CompletionTokens;
        return result;
    }

    /// <summary>
    /// Anthropic Claude'un belirtilen yeteneği destekleyip desteklemediğini kontrol eder
    /// Claude sadece sohbet ve tool use destekler
    /// </summary>
    public bool HasCapability(ProviderCapability capability)
    {
        // Claude sadece Chat capability'sini destekler
        // STT, Embedding, TTS vb. için fallback gerekli
        return capability == ProviderCapability.Chat;
    }

    /// <summary>
    /// Ses transkripsiyonu - Claude desteklemiyor (fallback gerekli)
    /// </summary>
    public async Task<string> TranscribeAudioAsync(string wavFilePath, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Claude STT desteklemiyor. Groq veya OpenAI fallback'i kullanın.");
    }

    /// <summary>
    /// Embedding - Claude desteklemiyor (fallback gerekli)
    /// </summary>
    public async Task<float[]> EmbedTextAsync(string text, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Claude Embedding desteklemiyor. Ollama veya OpenAI fallback'i kullanın.");
    }

    /// <summary>
    /// Text-to-Speech - Claude desteklemiyor
    /// </summary>
    public async Task<byte[]> GenerateSpeechAsync(string text, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Claude TTS desteklemiyor.");
    }

    /// <summary>
    /// Image Generation - Claude desteklemiyor
    /// </summary>
    public async Task<string> GenerateImageAsync(string prompt, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Claude Image Generation desteklemiyor.");
    }
}
