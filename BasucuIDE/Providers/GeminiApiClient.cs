using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace mdaiAgent;

public class GeminiApiClient : IAiProvider
{
    private readonly AppSettings _settings;
    private readonly HttpClient _httpClient;

    public string ModelName => _settings.GoogleModel;

    public GeminiApiClient(AppSettings settings, HttpClient? httpClient = null)
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
        var apiKey = _settings.GoogleApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Google Gemini API anahtarı ayarlanmamış.");

        var contents = messages
            .Where(m => !string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase))
            .Select(m => new
            {
                role = m.Role == "assistant" ? "model" : "user",
                parts = BuildParts(m)
            }).ToList();

        var request = new Dictionary<string, object?>
        {
            ["contents"] = contents,
            ["generationConfig"] = new { temperature = 0.7, maxOutputTokens = 8192 }
        };
        var system = messages.FirstOrDefault(m => string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase))?.Content;
        if (!string.IsNullOrWhiteSpace(system))
            request["systemInstruction"] = new { parts = new[] { new { text = system } } };
        if (tools.Count > 0)
            request["tools"] = new[] { new { functionDeclarations = tools.Select(t => new
            {
                name = t.Function.Name,
                description = t.Function.Description,
                parameters = t.Function.Parameters
            }).ToList() } };

        var baseUrl = _settings.GoogleBaseUrl.TrimEnd('/');
        baseUrl = baseUrl.Replace("/openai", "", StringComparison.OrdinalIgnoreCase);
        var url = $"{baseUrl}/models/{Uri.EscapeDataString(_settings.GoogleModel)}:generateContent?key={Uri.EscapeDataString(apiKey.Trim())}";
        using var response = await _httpClient.PostAsync(url, new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Gemini API hatası ({(int)response.StatusCode}): {body}");
        return ParseResponse(body);
    }

    public async Task<ExtendedChatResponse?> SendChatWithToolsStreamAsync(
        List<ExtendedChatMessage> messages, 
        List<ToolDefinition> tools, 
        Action<string>? onTokenReceived = null, 
        CancellationToken cancellationToken = default)
    {
        var apiKey = _settings.GoogleApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Google Gemini API anahtarı ayarlanmamış.");

        var request = BuildRequest(messages, tools);
        var baseUrl = _settings.GoogleBaseUrl.TrimEnd('/').Replace("/openai", "", StringComparison.OrdinalIgnoreCase);
        var url = $"{baseUrl}/models/{Uri.EscapeDataString(_settings.GoogleModel)}:streamGenerateContent?alt=sse&key={Uri.EscapeDataString(apiKey.Trim())}";
        using var response = await _httpClient.PostAsync(url, new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"), cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Gemini API hatası ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync(cancellationToken)}");

        var message = new ExtendedChatMessage { Role = "assistant", Content = "" };
        var text = new StringBuilder();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            var chunk = ParseResponse(line[5..].Trim());
            var chunkMessage = chunk.Choices?.FirstOrDefault()?.Message;
            if (chunkMessage == null) continue;
            if (!string.IsNullOrEmpty(chunkMessage.Content))
            {
                text.Append(chunkMessage.Content);
                onTokenReceived?.Invoke(chunkMessage.Content);
            }
            if (chunkMessage.ToolCalls != null)
                message.ToolCalls = chunkMessage.ToolCalls;
        }
        message.Content = text.ToString();
        return new ExtendedChatResponse { Choices = new List<ExtendedChatResponseChoice> { new() { Index = 0, Message = message } } };
    }

    private Dictionary<string, object?> BuildRequest(List<ExtendedChatMessage> messages, List<ToolDefinition> tools)
    {
        var request = new Dictionary<string, object?>
        {
            ["contents"] = messages.Where(m => !m.Role.Equals("system", StringComparison.OrdinalIgnoreCase)).Select(m => new { role = m.Role == "assistant" ? "model" : "user", parts = BuildParts(m) }).ToList(),
            ["generationConfig"] = new { temperature = 0.7, maxOutputTokens = 8192 }
        };
        var system = messages.FirstOrDefault(m => m.Role.Equals("system", StringComparison.OrdinalIgnoreCase))?.Content;
        if (!string.IsNullOrWhiteSpace(system)) request["systemInstruction"] = new { parts = new[] { new { text = system } } };
        if (tools.Count > 0) request["tools"] = new[] { new { functionDeclarations = tools.Select(t => new { name = t.Function.Name, description = t.Function.Description, parameters = t.Function.Parameters }).ToList() } };
        return request;
    }

    private static object[] BuildParts(ExtendedChatMessage message)
    {
        var parts = new List<object>();
        if (!string.IsNullOrEmpty(message.Content)) parts.Add(new { text = message.Content });
        if (message.ToolCalls != null)
            parts.AddRange(message.ToolCalls.Select(call => new { functionCall = new { name = call.Function.Name, args = ParseObject(call.Function.Arguments) } }));
        if (!string.IsNullOrEmpty(message.ToolCallId))
            parts.Add(new { functionResponse = new { name = message.ToolCallId, response = new { content = message.Content ?? "" } } });
        return parts.ToArray();
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
        var text = new StringBuilder();
        if (document.RootElement.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0 && candidates[0].TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts))
        {
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var textProp)) text.Append(textProp.GetString());
                if (part.TryGetProperty("functionCall", out var functionCall))
                {
                    message.ToolCalls ??= new List<ToolCall>();
                    message.ToolCalls.Add(new ToolCall
                    {
                        Id = "gemini_" + Guid.NewGuid().ToString("N")[..8],
                        ExtraContent = new ExtraContent(),
                        Function = new FunctionCall
                        {
                            Name = functionCall.GetProperty("name").GetString() ?? "",
                            Arguments = functionCall.TryGetProperty("args", out var args) ? args.GetRawText() : "{}"
                        }
                    });
                }
            }
        }
        message.Content = text.ToString();
        var result = new ExtendedChatResponse { Choices = new List<ExtendedChatResponseChoice> { new() { Index = 0, Message = message } } };
        if (document.RootElement.TryGetProperty("usageMetadata", out var usage))
            result.Usage = new ChatUsage
            {
                PromptTokens = usage.TryGetProperty("promptTokenCount", out var prompt) ? prompt.GetInt32() : 0,
                CompletionTokens = usage.TryGetProperty("candidatesTokenCount", out var completion) ? completion.GetInt32() : 0
            };
        if (result.Usage != null) result.Usage.TotalTokens = result.Usage.PromptTokens + result.Usage.CompletionTokens;
        return result;
    }

    /// <summary>
    /// Google Gemini'nin belirtilen yeteneği destekleyip desteklemediğini kontrol eder
    /// Gemini sadece sohbet ve tool use destekler
    /// </summary>
    public bool HasCapability(ProviderCapability capability)
    {
        // Gemini sadece Chat capability'sini destekler
        // STT, Embedding, TTS vb. için fallback gerekli
        return capability == ProviderCapability.Chat;
    }

    /// <summary>
    /// Ses transkripsiyonu - Gemini desteklemiyor (fallback gerekli)
    /// </summary>
    public async Task<string> TranscribeAudioAsync(string wavFilePath, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Gemini STT desteklemiyor. Groq veya OpenAI fallback'i kullanın.");
    }

    /// <summary>
    /// Embedding - Gemini desteklemiyor (fallback gerekli)
    /// </summary>
    public async Task<float[]> EmbedTextAsync(string text, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Gemini Embedding desteklemiyor. Ollama veya OpenAI fallback'i kullanın.");
    }

    /// <summary>
    /// Text-to-Speech - Gemini desteklemiyor
    /// </summary>
    public async Task<byte[]> GenerateSpeechAsync(string text, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Gemini TTS desteklemiyor.");
    }

    /// <summary>
    /// Image Generation - Gemini desteklemiyor
    /// </summary>
    public async Task<string> GenerateImageAsync(string prompt, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Gemini Image Generation desteklemiyor.");
    }
}
