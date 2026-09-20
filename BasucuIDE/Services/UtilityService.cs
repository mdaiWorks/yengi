using System;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace mdaiAgent.Services;



/// <summary>

/// Handles utility operations: user options, smart recovery, and miscellaneous tasks.

/// </summary>

public class UtilityService

{

    private readonly string? _projectFolder;

    private readonly Action<string> _terminalLog;

    private readonly Func<string, List<string>, Task<string>>? _askUserOptions;

    private readonly Func<string, List<string>, bool, Task<string>>? _askUserOptionsWithSelection;

    private readonly Action<string>? _updateOperationStep;



    public UtilityService(

        string? projectFolder,

        Action<string> terminalLog,

        Func<string, List<string>, Task<string>>? askUserOptions = null,

        Action<string>? updateOperationStep = null)

    {

        _projectFolder = projectFolder;

        _terminalLog = terminalLog;

        _askUserOptions = askUserOptions;

        _updateOperationStep = updateOperationStep;

    }



    public UtilityService(

        string? projectFolder,

        Action<string> terminalLog,

        Func<string, List<string>, bool, Task<string>> askUserOptions,

        Action<string>? updateOperationStep = null)

        : this(projectFolder, terminalLog, (Func<string, List<string>, Task<string>>?)null, updateOperationStep)

    {

        _askUserOptionsWithSelection = askUserOptions;

    }



    public async Task<ToolResult> AskUserOptionsAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var question = arguments.GetProperty("question").GetString();
        var optionsArray = arguments.GetProperty("options").EnumerateArray();
        var options = new List<string>();

        foreach (var opt in optionsArray)
        {
            cancellationToken.ThrowIfCancellationRequested();
            options.Add(opt.GetString() ?? "");
        }

        var allowMultiple = arguments.TryGetProperty("allowMultiple", out var multipleProperty) && multipleProperty.GetBoolean();

        if (string.IsNullOrWhiteSpace(question) || options.Count == 0)
            return new ToolResult { Success = false, Error = "Soru ve seçenekler zorunlu." };

        try
        {
            EventBus.Publish(new TimelineEvent { Type = TimelineEventType.Thinking, Message = "Kullanıcı seçimi bekleniyor." });
            _updateOperationStep?.Invoke($"❓ Kullanıcı seçimi: {question}");

            if (_askUserOptionsWithSelection != null)
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromMinutes(10));
                var answer = await _askUserOptionsWithSelection(question, options, allowMultiple).WaitAsync(cts.Token);
                _terminalLog(LocalizationManager.Instance.GetString("KullaniciSecimiAnswer2").Replace("{answer}", answer));
                EventBus.Publish(new TimelineEvent { Type = TimelineEventType.Thinking, IsCompleted = true, Message = "Kullanıcı seçimi alındı." });
                return new ToolResult { Success = true, Output = answer };
            }

            if (_askUserOptions != null)
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromMinutes(10));
                var answer = await _askUserOptions(question, options).WaitAsync(cts.Token);
                _terminalLog(LocalizationManager.Instance.GetString("KullaniciSecimiAnswer2").Replace("{answer}", answer));
                EventBus.Publish(new TimelineEvent { Type = TimelineEventType.Thinking, IsCompleted = true, Message = "Kullanıcı seçimi alındı." });
                return new ToolResult { Success = true, Output = answer };
            }

            // Fallback: return first option if no UI handler
            cancellationToken.ThrowIfCancellationRequested();
            _terminalLog($"⚠️ UI handler olmadan ilk seçenek döndürülüyor");
            EventBus.Publish(new TimelineEvent { Type = TimelineEventType.Thinking, IsCompleted = true, Message = "Varsayılan kullanıcı seçimi kullanıldı." });
            return new ToolResult { Success = true, Output = options[0] };
        }
        catch (OperationCanceledException)
        {
            EventBus.Publish(new TimelineEvent { Type = TimelineEventType.Failed, IsFailed = true, Message = "Kullanıcı seçimi iptal edildi." });
            return new ToolResult { Success = false, Error = "Kullanıcı seçimi zaman aşımına uğradı veya iptal edildi." };
        }
        catch (Exception ex)
        {
            EventBus.Publish(new TimelineEvent { Type = TimelineEventType.Failed, IsFailed = true, Message = "Kullanıcı seçimi alınamadı." });
            return new ToolResult { Success = false, Error = $"Kullanıcı seçimi hatası: {ex.Message}" };
        }
    }

    public Task<ToolResult> SmartRecoveryAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var errorMessage = arguments.TryGetProperty("errorMessage", out var messageProp)
            ? messageProp.GetString()
            : arguments.TryGetProperty("lastError", out var legacyErrorProp) ? legacyErrorProp.GetString() : null;
        var errorType = arguments.TryGetProperty("errorType", out var typeProp)
            ? typeProp.GetString()
            : arguments.TryGetProperty("failedCommand", out var commandProp) ? commandProp.GetString() : "UnknownError";

        try
        {
            EventBus.Publish(new TimelineEvent { Type = TimelineEventType.FixingError, Message = $"Kurtarma planı oluşturuluyor: {errorType}" });
            _updateOperationStep?.Invoke("🔧 Akıllı kurtarma başlatılıyor...");
            _terminalLog($"🔧 Kurtarma başlatıldı: {errorType}");

            // Smart recovery strategy based on error type
            var recoveryContext = $"{errorType} {errorMessage}";
            var recovery = recoveryContext.Contains("build", StringComparison.OrdinalIgnoreCase)
                ? "Build Hatası Tespit Edildi; proje yeniden derlenmeli."
                : errorType switch
            {
                "BuildError" => "Proje temizleme ve yeniden derleme deneniyor...",
                "TestFailure" => "Başarısız testler analiz ediliyor...",
                "PathError" => "Yol yapılandırması kontrol ediliyor...",
                "PermissionError" => "İzin sorunları çözülüyor...",
                _ => "Genel kurtarma prosedürü uygulanıyor..."
            };

            _terminalLog($"✓ Kurtarma: {recovery}");

            var result = new
            {
                Title = LocalizationManager.Instance.GetString("AkilliKurtarma"),
                Recovery = recovery,
                Status = "Ongoing",
                ErrorType = errorType,
                Message = errorMessage
            };

            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            EventBus.Publish(new TimelineEvent { Type = TimelineEventType.FixingError, IsCompleted = true, Message = "Kurtarma planı oluşturuldu." });
            return Task.FromResult(new ToolResult { Success = true, Output = json });
        }
        catch (OperationCanceledException)
        {
            EventBus.Publish(new TimelineEvent { Type = TimelineEventType.Failed, IsFailed = true, Message = "Kurtarma planı iptal edildi." });
            return Task.FromResult(new ToolResult { Success = false, Error = "Kurtarma kullanıcı tarafından iptal edildi." });
        }
        catch (Exception ex)
        {
            EventBus.Publish(new TimelineEvent { Type = TimelineEventType.Failed, IsFailed = true, Message = "Kurtarma planı oluşturulamadı." });
            return Task.FromResult(new ToolResult { Success = false, Error = $"Kurtarma hatası: {ex.Message}" });
        }
    }

}

