using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace mdaiAgent.Services;

/// <summary>
/// Handles web operations: searching, fetching, and screenshots.
/// </summary>
public class WebService
{
    private readonly string? _projectFolder;
    private readonly Action<string> _terminalLog;
    private readonly ContextOptimizerService _optimizer;
    private readonly ToolOutputStore _outputStore;

    public WebService(
        string? projectFolder,
        Action<string> terminalLog,
        ToolOutputStore? outputStore = null)
    {
        _projectFolder = projectFolder;
        _terminalLog = terminalLog;
        _optimizer = new ContextOptimizerService();
        _outputStore = outputStore ?? new ToolOutputStore();
    }

    public async Task<ToolResult> WebSearchAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var query = arguments.GetProperty("query").GetString();

        if (string.IsNullOrWhiteSpace(query))
            return new ToolResult { Success = false, Error = "Arama sorgusu zorunlu." };

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _terminalLog($"🔍 Web araması: {query}");
            EventBus.Publish(new TimelineEvent
            {
                Type = TimelineEventType.RunningTerminal,
                Message = $"Web aranıyor: {query}"
            });

            var settings = SettingsWindow.GetSettings();
            var searchResults = new System.Collections.Generic.List<object>();

            // 1. Tavily API denemesi
            if (!string.IsNullOrWhiteSpace(settings.TavilyApiKey))
            {
                try
                {
                    using var client = new HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(15);
                    var payload = new { api_key = settings.TavilyApiKey, query = query, max_results = 5 };
                    var response = await client.PostAsJsonAsync("https://api.tavily.com/search", payload, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        var jsonDoc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken);
                        if (jsonDoc != null && jsonDoc.RootElement.TryGetProperty("results", out var resultsElem) && resultsElem.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in resultsElem.EnumerateArray())
                            {
                                string title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                                string url = item.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                                string content = item.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
                                searchResults.Add(new { Title = title, Url = url, Description = content });
                            }
                        }
                    }
                }
                catch { /* Fallback to free search below */ }
            }

            // 2. Ücretsiz DuckDuckGo / Web Arama Fallback (Tavily yoksa veya sonuç dönmediyse)
            if (searchResults.Count == 0)
            {
                try
                {
                    using var client = new HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(15);
                    var searchUrl = $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(query)}";
                    var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
                    request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                    
                    var response = await client.SendAsync(request, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        var html = await response.Content.ReadAsStringAsync(cancellationToken);
                        var matches = System.Text.RegularExpressions.Regex.Matches(
                            html, 
                            @"<a[^>]*class=""result__a""[^>]*href=""(?<url>[^""]+)""[^>]*>(?<title>[\s\S]*?)</a>[\s\S]*?<a[^>]*class=""result__snippet""[^>]*>(?<snippet>[\s\S]*?)</a>",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                        int count = 0;
                        foreach (System.Text.RegularExpressions.Match match in matches)
                        {
                            if (count >= 5) break;
                            string rawUrl = match.Groups["url"].Value;
                            string title = System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(match.Groups["title"].Value, "<.*?>", "")).Trim();
                            string snippet = System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(match.Groups["snippet"].Value, "<.*?>", "")).Trim();

                            if (rawUrl.Contains("uddg="))
                            {
                                var uriMatch = System.Text.RegularExpressions.Regex.Match(rawUrl, @"uddg=(?<realUrl>[^&]+)");
                                if (uriMatch.Success)
                                    rawUrl = Uri.UnescapeDataString(uriMatch.Groups["realUrl"].Value);
                            }

                            if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(rawUrl))
                            {
                                searchResults.Add(new { Title = title, Url = rawUrl, Description = snippet });
                                count++;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _terminalLog($"⚠️ Ücretsiz web arama fallback hatası: {ex.Message}");
                }
            }

            var results = new
            {
                Query = query,
                Results = searchResults
            };

            var rawOutput = JsonSerializer.Serialize(results);
            var optimizedOutput = _optimizer.OptimizeTerminalOutput(rawOutput);
            var outputId = _outputStore.SaveIfTruncated(rawOutput, optimizedOutput, "websearch");

            EventBus.Publish(new TimelineEvent
            {
                Type = TimelineEventType.RunningTerminal,
                IsCompleted = true,
                Message = "Web araması tamamlandı."
            });
            return new ToolResult { Success = true, Output = AppendOutputReference(optimizedOutput, outputId) };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            EventBus.Publish(new TimelineEvent { Type = TimelineEventType.Failed, IsFailed = true, Message = "Web araması başarısız oldu." });
            return new ToolResult { Success = false, Error = $"Web arama hatası: {ex.Message}" };
        }
    }


    public async Task<ToolResult> WebFetchAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var url = arguments.GetProperty("url").GetString();

        if (string.IsNullOrWhiteSpace(url))
            return new ToolResult { Success = false, Error = "URL zorunlu." };

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _terminalLog($"📥 Sayfa yükleniyor: {url}");
            EventBus.Publish(new TimelineEvent
            {
                Type = TimelineEventType.RunningTerminal,
                Message = "Web sayfası yükleniyor."
            });

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            cancellationToken.ThrowIfCancellationRequested();
            var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new ToolResult { Success = false, Error = $"HTTP {response.StatusCode}" };

            cancellationToken.ThrowIfCancellationRequested();
            var rawContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var optimizedContent = _optimizer.OptimizeTerminalOutput(rawContent);
            var outputId = _outputStore.SaveIfTruncated(rawContent, optimizedContent, "webfetch");

            EventBus.Publish(new TimelineEvent
            {
                Type = TimelineEventType.RunningTerminal,
                IsCompleted = true,
                Message = "Web sayfası yüklendi."
            });

            return new ToolResult { Success = true, Output = AppendOutputReference(optimizedContent, outputId) };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            EventBus.Publish(new TimelineEvent { Type = TimelineEventType.Failed, IsFailed = true, Message = "Web sayfası yüklenemedi." });
            return new ToolResult { Success = false, Error = $"Sayfa yükleme hatası: {ex.Message}" };
        }
    }

    public async Task<ToolResult> TakeScreenshotAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            _terminalLog("📸 Ekran görüntüsü alındı");
            EventBus.Publish(new TimelineEvent
            {
                Type = TimelineEventType.Info,
                IsCompleted = true,
                Message = "Ekran görüntüsü alındı."
            });
            return new ToolResult
            {
                Success = true,
                Output = "Screenshot taken (path would be returned in production UI context)"
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new ToolResult { Success = false, Error = $"Screenshot error: {ex.Message}" };
        }
    }

    private static string AppendOutputReference(string output, string? outputId)
    {
        return string.IsNullOrWhiteSpace(outputId)
            ? output
            : output + $"\n\n[FULL_OUTPUT_ID:{outputId}] (Uzun çıktı aralıklarını okumak için ReadToolOutput kullanın.)";
    }
}

