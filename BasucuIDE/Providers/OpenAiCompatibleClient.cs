using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace mdaiAgent;

#pragma warning disable CS8602

public class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Content { get; set; } = "";
}

#pragma warning restore CS8602

public class ChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("messages")]
    public List<ExtendedChatMessage> Messages { get; set; } = new();

    [JsonPropertyName("tools")]
    public List<ToolDefinition>? Tools { get; set; }

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; } = 4096;

    [JsonPropertyName("temperature")]
    public double Temperature { get; set; } = 0.7;

    [JsonPropertyName("stream")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Stream { get; set; }
}

public class ChatResponseChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("message")]
    public ChatMessage? Message { get; set; }
}

public class ChatResponse
{
    [JsonPropertyName("choices")]
    public List<ChatResponseChoice>? Choices { get; set; }

    [JsonPropertyName("usage")]
    public ChatUsage? Usage { get; set; }

    [JsonPropertyName("error")]
    public object? Error { get; set; }
}

public class OpenAiCompatibleClient : IAiProvider
{
    public string ModelName => ResolveConnectionSettings().Model;

    private readonly HttpClient _httpClient;
    private readonly AppSettings _settings;

    private static string NormalizeAuthorizationHeader(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return string.Empty;

        var trimmed = apiKey.Trim();
        if (trimmed.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        return "Bearer " + trimmed;
    }

    // Allow injecting HttpClient for testing
    public OpenAiCompatibleClient(AppSettings settings, HttpClient? httpClient = null)
    {
        _settings = settings;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.ApiTimeoutSeconds > 0 ? settings.ApiTimeoutSeconds : 600));

        var activeKey = settings.GetActiveApiKey();
        if (!string.IsNullOrEmpty(activeKey))
        {
            var authHeaderValue = NormalizeAuthorizationHeader(activeKey);
            if (!_httpClient.DefaultRequestHeaders.Contains("Authorization") && !string.IsNullOrEmpty(authHeaderValue))
            {
                _httpClient.DefaultRequestHeaders.Add("Authorization", authHeaderValue);
            }
        }
    }

    public async Task<ExtendedChatResponse?> SendChatWithToolsAsync(List<ExtendedChatMessage> messages, List<ToolDefinition> tools, CancellationToken cancellationToken = default)
    {
        var connectionSettings = ResolveConnectionSettings();

        if (!connectionSettings.UseLocalModel && string.IsNullOrEmpty(_settings.GetActiveApiKey()))
        {
            throw new Exception("Lütfen ayarlardan API anahtarınızı ekleyin!");
        }

        var request = new ChatRequest
        {
            Model = connectionSettings.Model,
            Messages = messages,
            Tools = tools,
            MaxTokens = 8192,
            Temperature = 0.7
        };

        // Basic safeguard: prevent sending very large attachments inline.
        try
        {
            const long maxInlineBytes = 5 * 1024 * 1024; // 5 MB
            long totalBytes = 0;
            var attachmentsToUpload = new List<Attachment>();
            if (messages != null)
            {
                foreach (var m in messages)
                {
                    if (m.Attachments != null)
                    {
                        foreach (var a in m.Attachments)
                        {
                            if (string.IsNullOrEmpty(a.Base64Content))
                                continue;

                            var _b64 = a.Base64Content;
                            if (string.IsNullOrEmpty(_b64))
                                continue;

                            totalBytes += (long)(_b64.Length * 3.0 / 4.0);
                            if (totalBytes > maxInlineBytes)
                            {
                                // If settings provide an upload endpoint, try to upload attachments and replace with URLs
                                if (!string.IsNullOrEmpty(_settings?.BaseUrl) && !string.IsNullOrEmpty(_settings?.LocalBaseUrl) && false)
                                {
                                    // Placeholder - no automatic upload without explicit UploadEndpoint setting
                                }

                                // Collect attachments to potentially upload via external endpoint configured in settings
                                attachmentsToUpload.AddRange(m.Attachments.Where(x => !string.IsNullOrEmpty(x.Base64Content)));
                                break;
                            }
                        }
                    }
                }
            }

            if (attachmentsToUpload.Count > 0)
            {
                // If an upload endpoint is configured in settings (UploadEndpoint), attempt to upload each file and replace with url
                var uploadEndpoint = _settings?.GetType().GetProperty("UploadEndpoint")?.GetValue(_settings) as string;
                if (!string.IsNullOrEmpty(uploadEndpoint))
                {
                    foreach (var a in attachmentsToUpload)
                    {
                        try
                        {
                            var bytes = Convert.FromBase64String(a.Base64Content ?? string.Empty);
                            using var multipartContent = new MultipartFormDataContent();
                            var fileContent = new ByteArrayContent(bytes);
                            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(a.MimeType ?? "application/octet-stream");
                            multipartContent.Add(fileContent, "file", a.FileName);

                            var resp = await _httpClient.PostAsync(uploadEndpoint, multipartContent, cancellationToken);
                            var respJson = resp.Content != null ? await resp.Content.ReadAsStringAsync(cancellationToken) : string.Empty;
                            if (resp.IsSuccessStatusCode)
                            {
                                try
                                {
                                    using var doc = JsonDocument.Parse(respJson);
                                    if (doc.RootElement.TryGetProperty("url", out var urlElem))
                                    {
                                        var urlStr = urlElem.GetString();
                                        if (!string.IsNullOrEmpty(urlStr))
                                        {
                                            a.Url = urlStr;
                                            a.Base64Content = null; // clear inline content
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError($"Attachment upload failed: {ex.Message}");
                        }
                    }
                }

                // Recalculate totalBytes and if still exceeding, throw error
                long recalculated = 0;
                if (messages != null)
                {
                    foreach (var m in messages)
                    {
                        if (m.Attachments != null)
                        {
                            foreach (var a in m.Attachments)
                            {
                                if (string.IsNullOrEmpty(a.Base64Content))
                                    continue;

                                var _b64 = a.Base64Content;
                                if (string.IsNullOrEmpty(_b64))
                                    continue;

                                recalculated += (long)(_b64.Length * 3.0 / 4.0);
                            }
                        }
                    }
                }

                if (recalculated > maxInlineBytes)
                {
                    throw new Exception("Ekler çok büyük. Lütfen 5MB'den küçük dosyalar ekleyin veya Ayarlar -> UploadEndpoint yapılandırmasını kullanın.");
                }
            }
        }
        catch (Exception)
        {
            throw;
        }

        // Map ExtendedChatMessage to API payload format, supporting Vision API (Multimodal)
        var apiMessages = new List<object>();
        foreach (var m in messages ?? Enumerable.Empty<ExtendedChatMessage>())
        {
            if (m == null) continue;
            if (m.Attachments != null && m.Attachments.Count > 0)
            {
                var contentList = new List<object>();
                if (!string.IsNullOrEmpty(m.Content))
                {
                    contentList.Add(new { type = "text", text = m.Content });
                }
                
                foreach (var a in m.Attachments)
                {
                    if (!string.IsNullOrEmpty(a.Url))
                    {
                        contentList.Add(new { type = "image_url", image_url = new { url = a.Url } });
                    }
                    else if (!string.IsNullOrEmpty(a.Base64Content))
                    {
                        var mime = a.MimeType ?? "image/jpeg";
                        contentList.Add(new { type = "image_url", image_url = new { url = $"data:{mime};base64,{a.Base64Content}" } });
                    }
                }

                apiMessages.Add(new
                {
                    role = m.Role,
                    content = contentList,
                    tool_calls = m.ToolCalls,
                    tool_call_id = m.ToolCallId
                });
            }
            else
            {
                apiMessages.Add(new
                {
                    role = m.Role,
                    content = m.Content,
                    tool_calls = m.ToolCalls,
                    tool_call_id = m.ToolCallId
                });
            }
        }

        var apiRequest = new
        {
            model = connectionSettings.Model,
            messages = apiMessages,
            tools = tools,
            max_tokens = request.MaxTokens,
            temperature = request.Temperature,
            stream = request.Stream
        };

        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        var json = JsonSerializer.Serialize(apiRequest, options);
        if (json.Length > 800000)
        {
            throw new Exception($"Payload çok büyük ({(json.Length / 1024)}KB). Bu istek API tarafından 413 Payload Too Large hatası alabilir. Lütfen daha az dosya seçin veya isteği daraltın.");
        }
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Basit retry mekanizması: 3 deneme, exponential backoff
        int maxAttempts = 3;
        var rnd = new Random();
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                Logger.LogInfo($"API request attempt {attempt} to {connectionSettings.BaseUrl}");
                var requestUrl = connectionSettings.BaseUrl.TrimEnd('/') + "/chat/completions";
                
                var _at = _settings?.ApiTimeoutSeconds ?? 600;
                var timeoutSeconds = Math.Max(1, _at > 0 ? _at : 600);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);
                var response = await _httpClient.PostAsync(requestUrl, content, linkedCts.Token);

                var responseJson = response.Content != null ? await response.Content.ReadAsStringAsync() : string.Empty;
                stopwatch.Stop();

                if (response.IsSuccessStatusCode)
                {
                    var parsed = JsonSerializer.Deserialize<ExtendedChatResponse>(responseJson);
                    if (parsed != null && parsed.Choices != null && parsed.Choices.Count > 0)
                    {
                        var assistant = parsed.Choices[0].Message;
                        if (assistant != null && string.IsNullOrEmpty(assistant.Content))
                        {
                            var extracted = ExtractTextFromJsonResponse(responseJson);
                            if (!string.IsNullOrWhiteSpace(extracted))
                            {
                                assistant.Content = extracted;
                            }
                        }
                    }
                    if (parsed?.Choices != null && parsed.Choices.Count > 0)
                    {
                        ExtractHallucinatedXmlToolCalls(parsed.Choices[0].Message);
                    }
                    if (parsed != null)
                    {
                        parsed.TotalLatencyMs = stopwatch.ElapsedMilliseconds;
                        parsed.TtftMs = stopwatch.ElapsedMilliseconds;
                    }
                    return parsed;
                }
                else
                {
                    if (attempt == maxAttempts)
                    {
                        Logger.LogError($"API final failure: {response.StatusCode} {responseJson}");
                        throw new Exception($"API hata: {response.StatusCode} - {responseJson}");
                    }
                    else
                    {
                        Logger.LogInfo($"API transient failure (attempt {attempt}): {response.StatusCode}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                var _at = _settings?.ApiTimeoutSeconds ?? 600;
                var timeoutSeconds = Math.Max(1, _at > 0 ? _at : 600);
                Logger.LogError($"API timeout after {timeoutSeconds} seconds (attempt {attempt})");
                if (attempt == maxAttempts)
                {
                    throw new Exception($"API isteği zaman aşımına uğradı ({timeoutSeconds} saniye). Lütfen daha sonra tekrar deneyin.");
                }

                var delay = TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1) + rnd.Next(100, 500));
                Logger.LogInfo($"API timeout on attempt {attempt}, retrying after {delay.TotalMilliseconds}ms");
                await Task.Delay(delay, cancellationToken);
                continue;
            }
            catch (Exception) when (attempt < maxAttempts)
            {
                // bekle ve yeniden dene
                var delay = TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1) + rnd.Next(100, 500));
                Logger.LogInfo($"API exception on attempt {attempt}, retrying after {delay.TotalMilliseconds}ms");
                await Task.Delay(delay, cancellationToken);
                continue;
            }
        }

        return null;
    }

    public async Task<ExtendedChatResponse?> SendChatWithToolsStreamAsync(List<ExtendedChatMessage> messages, List<ToolDefinition> tools, Action<string>? onTokenReceived = null, CancellationToken cancellationToken = default)
    {
        var connectionSettings = ResolveConnectionSettings();

        if (!connectionSettings.UseLocalModel && string.IsNullOrEmpty(_settings.GetActiveApiKey()))
        {
            throw new Exception("Lütfen ayarlardan API anahtarınızı ekleyin!");
        }

        var request = new ChatRequest
        {
            Model = connectionSettings.Model,
            Messages = messages,
            Tools = tools,
            MaxTokens = 8192,
            Temperature = 0.7,
            Stream = true
        };

        // Map ExtendedChatMessage to API payload format, supporting Vision API (Multimodal)
        var apiMessages = new List<object>();
        foreach (var m in messages)
        {
            if (m.Attachments != null && m.Attachments.Count > 0)
            {
                var contentList = new List<object>();
                if (!string.IsNullOrEmpty(m.Content))
                {
                    contentList.Add(new { type = "text", text = m.Content });
                }
                
                foreach (var a in m.Attachments)
                {
                    if (!string.IsNullOrEmpty(a.Url))
                    {
                        contentList.Add(new { type = "image_url", image_url = new { url = a.Url } });
                    }
                    else if (!string.IsNullOrEmpty(a.Base64Content))
                    {
                        var mime = a.MimeType ?? "image/jpeg";
                        contentList.Add(new { type = "image_url", image_url = new { url = $"data:{mime};base64,{a.Base64Content}" } });
                    }
                }

                apiMessages.Add(new
                {
                    role = m.Role,
                    content = contentList,
                    tool_calls = m.ToolCalls,
                    tool_call_id = m.ToolCallId
                });
            }
            else
            {
                apiMessages.Add(new
                {
                    role = m.Role,
                    content = m.Content,
                    tool_calls = m.ToolCalls,
                    tool_call_id = m.ToolCallId
                });
            }
        }

        var apiRequest = new
        {
            model = connectionSettings.Model,
            messages = apiMessages,
            tools = tools,
            max_tokens = request.MaxTokens,
            temperature = request.Temperature,
            stream = request.Stream
        };

        var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        var json = JsonSerializer.Serialize(apiRequest, options);
        
        if (json.Length > 800000)
        {
            throw new Exception($"Payload çok büyük ({(json.Length / 1024)}KB). Bu istek API tarafından 413 Payload Too Large hatası alabilir. Lütfen daha az dosya seçin veya isteği daraltın.");
        }
        
        var requestUrl = connectionSettings.BaseUrl.TrimEnd('/') + "/chat/completions";
        
        int maxAttempts = 3;
        var rnd = new Random();
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                long? ttftMs = null;
                
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var timeoutSeconds = Math.Max(1, _settings.ApiTimeoutSeconds > 0 ? _settings.ApiTimeoutSeconds : 600);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)); 
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);
                
                var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUrl) { Content = content };
                var response = await _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);
                
                if (!response.IsSuccessStatusCode)
                {
                    if (attempt == maxAttempts || (response.StatusCode != System.Net.HttpStatusCode.TooManyRequests && response.StatusCode != System.Net.HttpStatusCode.InternalServerError))
                    {
                        var errJson = await response.Content.ReadAsStringAsync();
                        
                        // OLLAMA / LM STUDIO / LOCAL TEXT MODEL FALLBACK
                        if (response.StatusCode == System.Net.HttpStatusCode.NotFound && (errJson.Contains("image") || errJson.Contains("No endpoints found")))
                        {
                            foreach (var msg in messages)
                            {
                                msg.Attachments = null; // Clean history to prevent loop
                            }
                            throw new Exception("API görüntü desteklemiyor. Geçmiş mesajlardaki resimler otomatik temizlendi. Lütfen isteğinizi tekrar yazın.");
                        }
                        
                        throw new Exception($"API hata: {response.StatusCode} - {errJson}");
                    }
                    var delayFail = TimeSpan.FromMilliseconds(1000 * Math.Pow(2, attempt - 1) + rnd.Next(100, 1000));
                    Logger.LogInfo($"API streaming failure {response.StatusCode}, retrying after {delayFail.TotalMilliseconds}ms");
                    await Task.Delay(delayFail, cancellationToken);
                    continue;
                }
                
                using var stream = await response.Content.ReadAsStreamAsync(linkedCts.Token);
                var fullContent = new StringBuilder();
                var toolCalls = new Dictionary<int, ToolCall>();
                var sb = new StringBuilder();
                int chunkCount = 0;
                const int maxStreamingChars = 200_000;

                var buffer = new byte[8192];
                bool done = false;
                ChatUsage? streamUsage = null;

                while (!done)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var read = await stream.ReadAsync(buffer, linkedCts.Token);
                    if (read == 0) break;
                    var chunk = Encoding.UTF8.GetString(buffer, 0, read);
                    sb.Append(chunk);
                    chunkCount++;

                    if (chunkCount <= 6)
                    {
                        try
                        {
                            var preview = sb.Length > 400 ? sb.ToString(0, 400) + "..." : sb.ToString();
                            Console.WriteLine($"[Streaming raw #{chunkCount}] len={read} preview={preview}");
                            Logger.LogInfo($"[Streaming raw #{chunkCount}] len={read} preview={(preview.Length > 200 ? preview.Substring(0, 200) + "..." : preview)}");
                        }
                        catch { }
                    }

                    ProcessStreamBuffer();

                    if (fullContent.Length > maxStreamingChars)
                    {
                        Logger.LogInfo($"Streaming content exceeded {maxStreamingChars} chars, breaking stream.");
                        break;
                    }
                }

        void ProcessStreamBuffer()
        {
            while (true)
            {
                var bufferText = sb.ToString();
                if (string.IsNullOrWhiteSpace(bufferText))
                    return;

                // Try to parse SSE-style top-level events first
                var eventEnd = bufferText.IndexOf("\n\n", StringComparison.Ordinal);
                if (eventEnd >= 0)
                {
                    var eventText = bufferText.Substring(0, eventEnd);
                    sb.Remove(0, eventEnd + 2);
                    ProcessEvent(eventText);
                    if (done) return;
                    continue;
                }

                // If no SSE delimiter yet, attempt to parse raw JSON objects that may already be complete
                if (TryExtractJsonObject(ref bufferText, out var jsonObject, out var consumedLength))
                {
                    sb.Remove(0, consumedLength);
                    ProcessJsonChunk(jsonObject);
                    if (done) return;
                    continue;
                }

                return;
            }
        }

        void ProcessEvent(string eventText)
        {
            var lines = eventText.Replace("\r\n", "\n").Split('\n');
            foreach (var line in lines)
            {
                if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    var data = line.Substring(5).Trim();
                    if (data == "[DONE]")
                    {
                        done = true;
                        return;
                    }

                    if (!string.IsNullOrEmpty(data))
                    {
                        ProcessJsonChunk(data);
                        if (done) return;
                    }
                }
            }
        }

        bool TryExtractJsonObject(ref string text, out string jsonObject, out int consumed)
        {
            jsonObject = string.Empty;
            consumed = 0;
            var firstBrace = text.IndexOf('{');
            if (firstBrace < 0)
                return false;

            int depth = 0;
            for (int i = firstBrace; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        jsonObject = text.Substring(firstBrace, i - firstBrace + 1);
                        consumed = i + 1;
                        return true;
                    }
                }
            }

            return false;
        }

        void ProcessJsonChunk(string jsonChunk)
        {
            if (string.IsNullOrWhiteSpace(jsonChunk))
                return;

            var cleaned = jsonChunk.Trim();
            if (cleaned.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned.Substring(5).Trim();

            if (cleaned.Length == 0)
                return;

            try
            {
                using var doc = JsonDocument.Parse(cleaned);
                
                // 1. Extract text content
                var extracted = ExtractTextFromJsonElement(doc.RootElement);
                if (!string.IsNullOrEmpty(extracted))
                {
                    if (ttftMs == null) ttftMs = stopwatch.ElapsedMilliseconds;
                    fullContent.Append(extracted);
                    try { onTokenReceived?.Invoke(extracted); } catch { }
                }
                
                // 2. Extract tool calls from stream chunks (delta.tool_calls format)
                ExtractToolCallsFromChunk(doc.RootElement);
                
                // If we got tool calls but no text, record TTFT
                if (ttftMs == null && toolCalls.Count > 0)
                {
                    ttftMs = stopwatch.ElapsedMilliseconds;
                }

                // 3. Extract usage (some APIs send this in the last chunk)
                if (doc.RootElement.TryGetProperty("usage", out var usageElement) && usageElement.ValueKind == JsonValueKind.Object)
                {
                    try
                    {
                        streamUsage = JsonSerializer.Deserialize<ChatUsage>(usageElement.GetRawText());
                    }
                    catch { }
                }
                
                // Only log if neither text nor tool calls found
                if (string.IsNullOrEmpty(extracted) && toolCalls.Count == 0 && streamUsage == null)
                {
                    Logger.LogInfo($"[Streaming parse] parsed event but no text or tool_calls found: {cleaned.Substring(0, Math.Min(200, cleaned.Length))}");
                }
            }
            catch (JsonException)
            {
                Logger.LogInfo($"[Streaming parse] invalid JSON chunk ignored: {cleaned.Substring(0, Math.Min(200, cleaned.Length))}");
            }
        }

        void ExtractToolCallsFromChunk(JsonElement element)
        {
            // Handle OpenAI-compatible streaming format: choices[0].delta.tool_calls[]
            // or choices[0].delta.tool_calls[0].function.name / arguments
            if (element.ValueKind != JsonValueKind.Object)
                return;

            // Try to find choices array
            if (!element.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
                return;

            foreach (var choice in choices.EnumerateArray())
            {
                // Try to find delta (streaming) or message (non-streaming)
                JsonElement delta;
                if (choice.TryGetProperty("delta", out var d))
                    delta = d;
                else if (choice.TryGetProperty("message", out var m))
                    delta = m;
                else
                    continue;

                if (delta.ValueKind != JsonValueKind.Object)
                    continue;

                // Check for tool_calls in delta
                if (!delta.TryGetProperty("tool_calls", out var toolCallsArray) || toolCallsArray.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var tc in toolCallsArray.EnumerateArray())
                {
                    if (tc.ValueKind != JsonValueKind.Object)
                        continue;

                    // Get tool call index
                    int index = 0;
                    if (tc.TryGetProperty("index", out var idxProp) && idxProp.ValueKind == JsonValueKind.Number)
                    {
                        index = idxProp.GetInt32();
                    }

                    // Get or create ToolCall for this index
                    if (!toolCalls.TryGetValue(index, out var existingCall))
                    {
                        existingCall = new ToolCall
                        {
                            Id = tc.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String
                                ? idProp.GetString() ?? Guid.NewGuid().ToString("N")
                                : Guid.NewGuid().ToString("N"),
                            Type = "function",
                            Function = new FunctionCall()
                        };
                        toolCalls[index] = existingCall;
                    }

                    // Update function name (may come in a separate chunk)
                    if (tc.TryGetProperty("function", out var funcProp) && funcProp.ValueKind == JsonValueKind.Object)
                    {
                        if (funcProp.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                        {
                            var nameVal = nameProp.GetString();
                            if (!string.IsNullOrEmpty(nameVal))
                                existingCall.Function.Name += nameVal;
                        }

                        if (funcProp.TryGetProperty("arguments", out var argsProp) && argsProp.ValueKind == JsonValueKind.String)
                        {
                            var argsVal = argsProp.GetString();
                            if (!string.IsNullOrEmpty(argsVal))
                                existingCall.Function.Arguments += argsVal;
                        }
                    }
                }
            }
        }

        string ExtractTextFromJsonElement(JsonElement element)
        {
            var builder = new StringBuilder();
            ExtractTextRecursive(element, builder);
            return builder.ToString();

            static void ExtractTextRecursive(JsonElement node, StringBuilder sb)
            {
                switch (node.ValueKind)
                {
                    case JsonValueKind.Object:
                        foreach (var property in node.EnumerateObject())
                        {
                            if ((property.NameEquals("content") || property.NameEquals("text")) && property.Value.ValueKind == JsonValueKind.String)
                            {
                                var value = property.Value.GetString();
                                if (!string.IsNullOrEmpty(value))
                                {
                                    sb.Append(value);
                                }
                            }
                            else if (!property.NameEquals("tool_calls"))
                            {
                                // Skip tool_calls in recursive search (handled separately)
                                ExtractTextRecursive(property.Value, sb);
                            }
                        }
                        break;
                    case JsonValueKind.Array:
                        foreach (var item in node.EnumerateArray())
                        {
                            ExtractTextRecursive(item, sb);
                        }
                        break;
                }
            }
        }
        
        try
        {
            Logger.LogInfo($"Streaming finished: totalLen={fullContent.Length} toolCalls={toolCalls.Count} chunks={chunkCount}");
            if (fullContent.Length == 0 && chunkCount > 0)
            {
                // Provide extra diagnostic info only when streaming produced no content
                var raw = sb.Length > 2000 ? sb.ToString(0, 2000) + "..." : sb.ToString();
                Logger.LogInfo($"[Streaming diagnostic] no content extracted from stream; sample raw buffer:\n{raw}");
            }
        }
        catch { }

        stopwatch.Stop();
        
        var finalResponse = new ExtendedChatResponse
        {
            Choices = new List<ExtendedChatResponseChoice>
            {
                new ExtendedChatResponseChoice
                {
                    Message = new ExtendedChatMessage
                    {
                        Role = "assistant",
                        Content = fullContent.ToString(),
                        ToolCalls = toolCalls.Count > 0 ? toolCalls.Values.ToList() : null
                    }
                }
            },
            Usage = streamUsage,
            TtftMs = ttftMs ?? stopwatch.ElapsedMilliseconds,
            TotalLatencyMs = stopwatch.ElapsedMilliseconds
        };

        if (finalResponse.Choices != null && finalResponse.Choices.Count > 0)
        {
            ExtractHallucinatedXmlToolCalls(finalResponse.Choices[0].Message);
        }
        return finalResponse;
    }
    catch (OperationCanceledException)
    {
        var delayFail = TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1) + rnd.Next(100, 500));
        Logger.LogError($"API streaming timeout on attempt {attempt}, retrying after {delayFail.TotalMilliseconds}ms");
        if (attempt == maxAttempts)
            throw new Exception($"API isteği zaman aşımına uğradı. Lütfen daha sonra tekrar deneyin.");
        await Task.Delay(delayFail, cancellationToken);
    }
    catch (Exception ex)
    {
        var delayFail = TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1) + rnd.Next(100, 500));
        Logger.LogError($"API streaming exception on attempt {attempt}: {ex.Message}");
        if (attempt == maxAttempts)
            throw;
        await Task.Delay(delayFail, cancellationToken);
    }
}
return null;
}

    private static string ExtractTextFromJsonResponse(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var builder = new StringBuilder();
            void Recurse(JsonElement element)
            {
                switch (element.ValueKind)
                {
                    case JsonValueKind.Object:
                        foreach (var prop in element.EnumerateObject())
                        {
                            if ((prop.NameEquals("content") || prop.NameEquals("text")) && prop.Value.ValueKind == JsonValueKind.String)
                            {
                                var value = prop.Value.GetString();
                                if (!string.IsNullOrEmpty(value))
                                    builder.Append(value);
                            }
                            else
                            {
                                Recurse(prop.Value);
                            }
                        }
                        break;
                    case JsonValueKind.Array:
                        foreach (var item in element.EnumerateArray())
                        {
                            Recurse(item);
                        }
                        break;
                }
            }
            Recurse(doc.RootElement);
            return builder.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    public (string BaseUrl, string Model, bool UseLocalModel) ResolveConnectionSettings()
    {
        var isLocal = _settings.ActiveProvider == ProviderType.LocalModel || _settings.UseLocalModel;
        var baseUrl = isLocal ? _settings.LocalBaseUrl : _settings.GetActiveBaseUrl();
        var model = isLocal ? _settings.LocalModel : _settings.GetActiveModel();
        return (baseUrl, model, isLocal);
    }

    private void ExtractHallucinatedXmlToolCalls(ExtendedChatMessage? assistantMessage)
    {
        if (assistantMessage == null || string.IsNullOrWhiteSpace(assistantMessage.Content)) return;

        ExtractDsmlToolCalls(assistantMessage);
        if (assistantMessage.ToolCalls?.Count > 0)
            return;

        var functionRegex = new System.Text.RegularExpressions.Regex(@"<function=([^>]+)>(.*?)</function>", System.Text.RegularExpressions.RegexOptions.Singleline);
        var parameterRegex = new System.Text.RegularExpressions.Regex(@"<parameter=([^>]+)>(.*?)</parameter>", System.Text.RegularExpressions.RegexOptions.Singleline);

        var matches = functionRegex.Matches(assistantMessage.Content);
        if (matches.Count > 0)
        {
            assistantMessage.ToolCalls ??= new List<ToolCall>();
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                var functionName = match.Groups[1].Value.Trim();
                var functionBody = match.Groups[2].Value;
                
                var arguments = new Dictionary<string, object>();
                var paramMatches = parameterRegex.Matches(functionBody);
                foreach (System.Text.RegularExpressions.Match pMatch in paramMatches)
                {
                    var paramName = pMatch.Groups[1].Value.Trim();
                    var paramValue = pMatch.Groups[2].Value.Trim();
                    arguments[paramName] = paramValue;
                }

                var toolCall = new ToolCall
                {
                    Id = "call_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                    Type = "function",
                    Function = new FunctionCall
                    {
                        Name = functionName,
                        Arguments = System.Text.Json.JsonSerializer.Serialize(arguments)
                    }
                };
                assistantMessage.ToolCalls.Add(toolCall);
            }

            assistantMessage.Content = functionRegex.Replace(assistantMessage.Content, string.Empty);
            assistantMessage.Content = assistantMessage.Content.Replace("</tool_call>", "").Trim();
        }
    }

    private static void ExtractDsmlToolCalls(ExtendedChatMessage assistantMessage)
    {
        var content = assistantMessage.Content ?? string.Empty;
        var invokeRegex = new System.Text.RegularExpressions.Regex(
            @"(?:<｜DSML｜|<DSML>)invoke\s+name=[""']([^""']+)[""']>(.*?)(?:</invoke>|</｜DSML｜invoke>|</DSML>invoke>)",
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var parameterRegex = new System.Text.RegularExpressions.Regex(
            @"(?:<｜DSML｜|<DSML>)parameter\s+name=[""']([^""']+)[""'][^>]*>(.*?)(?:</parameter>|</｜DSML｜parameter>|</DSML>parameter>)",
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        var matches = invokeRegex.Matches(content);
        if (matches.Count == 0)
            return;

        assistantMessage.ToolCalls ??= new List<ToolCall>();
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var arguments = new Dictionary<string, string>();
            foreach (System.Text.RegularExpressions.Match parameter in parameterRegex.Matches(match.Groups[2].Value))
                arguments[parameter.Groups[1].Value.Trim()] = parameter.Groups[2].Value.Trim();

            assistantMessage.ToolCalls.Add(new ToolCall
            {
                Id = "call_" + Guid.NewGuid().ToString("N")[..8],
                Type = "function",
                Function = new FunctionCall
                {
                    Name = match.Groups[1].Value.Trim(),
                    Arguments = JsonSerializer.Serialize(arguments)
                }
            });
        }

        assistantMessage.Content = invokeRegex.Replace(content, string.Empty).Trim();
        assistantMessage.Content = System.Text.RegularExpressions.Regex.Replace(
            assistantMessage.Content,
            @"(?:<｜DSML｜|<DSML>)(?:tool_calls|/tool_calls)",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
    }

    /// <summary>
    /// Provider'ın belirtilen yeteneği destekleyip desteklemediğini kontrol eder
    /// </summary>
    public bool HasCapability(ProviderCapability capability)
    {
        var providerCaps = ProviderCapabilities.GetCapabilities(_settings.ActiveProvider);
        return (providerCaps & capability) == capability;
    }

    /// <summary>
    /// Ses dosyasını metne dönüştürür (Speech-to-Text)
    /// OpenAI Whisper API kullanır
    /// </summary>
    public async Task<string> TranscribeAudioAsync(string wavFilePath, CancellationToken cancellationToken = default)
    {
        var connectionSettings = ResolveConnectionSettings();
        
        if (!File.Exists(wavFilePath))
            throw new FileNotFoundException($"Ses dosyası bulunamadı: {wavFilePath}");

        // OpenAI/OpenAI-compatible endpoint'e Whisper isteği gönder
        using var form = new MultipartFormDataContent();
        using var audioStream = File.OpenRead(wavFilePath);
        var streamContent = new StreamContent(audioStream);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
        
        form.Add(streamContent, "file", Path.GetFileName(wavFilePath));
        form.Add(new StringContent("whisper-1"), "model");
        form.Add(new StringContent("tr"), "language");  // Türkçe

        var transcriptionUrl = $"{connectionSettings.BaseUrl}/audio/transcriptions";
        
        try
        {
            var response = await _httpClient.PostAsync(transcriptionUrl, form, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(responseContent);
            
            if (doc.RootElement.TryGetProperty("text", out var textElement))
                return textElement.GetString() ?? "";

            throw new Exception("API yanıtında 'text' alanı bulunamadı");
        }
        catch (Exception ex)
        {
            throw new Exception($"Ses transkripsiyonu başarısız: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Metni vektöre dönüştürür (Embedding)
    /// OpenAI text-embedding API kullanır
    /// </summary>
    public async Task<float[]> EmbedTextAsync(string text, CancellationToken cancellationToken = default)
    {
        var connectionSettings = ResolveConnectionSettings();

        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Embedding için metin boş olamaz");

        var request = new
        {
            model = "text-embedding-3-small",  // OpenAI default embedding model
            input = text
        };

        var requestJson = JsonSerializer.Serialize(request);
        var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        var embeddingUrl = $"{connectionSettings.BaseUrl}/embeddings";

        try
        {
            var response = await _httpClient.PostAsync(embeddingUrl, content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(responseContent);

            if (doc.RootElement.TryGetProperty("data", out var dataElement) && dataElement.GetArrayLength() > 0)
            {
                var firstElement = dataElement[0];
                if (firstElement.TryGetProperty("embedding", out var embeddingElement))
                {
                    var embedding = embeddingElement.EnumerateArray()
                        .Select(e => e.GetSingle())
                        .ToArray();
                    return embedding;
                }
            }

            throw new Exception("API yanıtında embedding alanı bulunamadı");
        }
        catch (Exception ex)
        {
            throw new Exception($"Embedding oluşturma başarısız: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Metni sese dönüştürür (Text-to-Speech)
    /// OpenAI Text-to-Speech API kullanır
    /// </summary>
    public async Task<byte[]> GenerateSpeechAsync(string text, CancellationToken cancellationToken = default)
    {
        var connectionSettings = ResolveConnectionSettings();

        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("TTS için metin boş olamaz");

        var request = new
        {
            model = "tts-1",
            input = text,
            voice = "alloy"
        };

        var requestJson = JsonSerializer.Serialize(request);
        var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        var ttsUrl = $"{connectionSettings.BaseUrl}/audio/speech";

        try
        {
            var response = await _httpClient.PostAsync(ttsUrl, content, cancellationToken);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            throw new Exception($"TTS oluşturma başarısız: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Metinden görüntü üretir (Image Generation)
    /// OpenAI DALL-E API kullanır
    /// </summary>
    public async Task<string> GenerateImageAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var connectionSettings = ResolveConnectionSettings();

        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("Görüntü üretimi için prompt boş olamaz");

        var request = new
        {
            model = "dall-e-3",
            prompt = prompt,
            n = 1,
            size = "1024x1024"
        };

        var requestJson = JsonSerializer.Serialize(request);
        var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        var imageUrl = $"{connectionSettings.BaseUrl}/images/generations";

        try
        {
            var response = await _httpClient.PostAsync(imageUrl, content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(responseContent);

            if (doc.RootElement.TryGetProperty("data", out var dataElement) && dataElement.GetArrayLength() > 0)
            {
                var firstElement = dataElement[0];
                if (firstElement.TryGetProperty("url", out var urlElement))
                    return urlElement.GetString() ?? "";
            }

            throw new Exception("API yanıtında resim URL'si bulunamadı");
        }
        catch (Exception ex)
        {
            throw new Exception($"Görüntü oluşturma başarısız: {ex.Message}", ex);
        }
    }
}
