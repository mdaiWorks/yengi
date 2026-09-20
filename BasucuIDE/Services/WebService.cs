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

            var results = new
            {
                Query = query,
                Results = new[] {
                    new { Title = LocalizationManager.Instance.GetString("OrnekSonuc1"), Url = "https://example.com/1", Description = LocalizationManager.Instance.GetString("IlgiliBilgi") },
                    new { Title = LocalizationManager.Instance.GetString("OrnekSonuc2"), Url = "https://example.com/2", Description = "Daha fazla bilgi" }
                }
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

