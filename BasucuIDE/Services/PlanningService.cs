using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace mdaiAgent.Services;

public sealed class ProjectMemoryDocument
{
    public int SchemaVersion { get; set; } = 2;
    public string UpdatedAt { get; set; } = DateTime.UtcNow.ToString("O");
    public string LastTask { get; set; } = "";
    public string LastPlan { get; set; } = "";
    public Dictionary<string, string> Architecture { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Conventions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Archived { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Kalıcı mimari karar kaydı — decisions.json'da serileştirilir.
/// Tip güvenliği sağlar; bozuk JSON'da verinin sessizce kaybolmasını önler.
/// </summary>
public sealed record DecisionRecord(
    [property: JsonPropertyName("date")]     string Date,
    [property: JsonPropertyName("topic")]    string Topic,
    [property: JsonPropertyName("decision")] string Decision,
    [property: JsonPropertyName("reason")]   string Reason
);

/// <summary>
/// SearchProjectMemory tarafından dönen kategorik arama sonucu.
/// </summary>
public sealed record MemorySearchResult(
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("key")]      string Key,
    [property: JsonPropertyName("value")]    string Value
);

/// <summary>
/// Handles plan creation, execution, and project memory operations.
/// Delegates AI plan generation to AiPlanGenerator (isolated from main conversation).
/// </summary>
public class PlanningService
{
    private const int CurrentMemorySchemaVersion = 2;
    private readonly string? _projectFolder;
    private readonly Action<string> _terminalLog;
    private readonly SubAgentResultCoordinator _subAgentCoordinator;
    private readonly ContextOptimizerService _optimizer;
    private readonly AiPlanGenerator? _aiPlanGenerator;

    public PlanningService(
        string? projectFolder,
        Action<string> terminalLog,
        SubAgentResultCoordinator subAgentCoordinator,
        AiPlanGenerator? aiPlanGenerator = null)
    {
        _projectFolder = projectFolder;
        _terminalLog = terminalLog;
        _subAgentCoordinator = subAgentCoordinator;
        _optimizer = new ContextOptimizerService();
        _aiPlanGenerator = aiPlanGenerator;
    }

    private string GetMemoryFilePath() => Path.Combine(_projectFolder ?? ".", ".mdai", "memory.json");

    private ProjectMemoryDocument LoadMemoryDocument()
    {
        var memoryFile = GetMemoryFilePath();
        if (!File.Exists(memoryFile)) return new ProjectMemoryDocument();

        try
        {
            var document = JsonSerializer.Deserialize<ProjectMemoryDocument>(
                File.ReadAllText(memoryFile),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return document ?? new ProjectMemoryDocument();
        }
        catch
        {
            return new ProjectMemoryDocument();
        }
    }

    private void SaveMemoryDocument(ProjectMemoryDocument document)
    {
        document.SchemaVersion = CurrentMemorySchemaVersion;
        document.UpdatedAt = DateTime.UtcNow.ToString("O");
        var memoryDirectory = Path.GetDirectoryName(GetMemoryFilePath())!;
        Directory.CreateDirectory(memoryDirectory);
        File.WriteAllText(
            GetMemoryFilePath(),
            JsonSerializer.Serialize(document, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
    }

    public async Task<ToolResult> CreatePlanAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var objective = arguments.TryGetProperty("task", out var taskProp) 
            ? taskProp.GetString() 
            : arguments.TryGetProperty("objective", out var objProp) ? objProp.GetString() : null;
        var context = arguments.TryGetProperty("context", out var ctxProp) ? ctxProp.GetString() : "";

        if (string.IsNullOrWhiteSpace(objective))
            return new ToolResult { Success = false, Error = "Amaç belirtilmelidir." };

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            EventBus.Publish(new TimelineEvent { Type = TimelineEventType.Thinking, Message = "Plan oluşturuluyor." });
            var planDirectory = Path.Combine(_projectFolder ?? ".", ".mdai", "plans");
            Directory.CreateDirectory(planDirectory);

            var planId = Guid.NewGuid().ToString()[..8];
            var planFile = Path.Combine(planDirectory, $"plan_{planId}.json");

            string planContent;
            if (_aiPlanGenerator != null)
            {
                try
                {
                    _terminalLog("🤖 AI plan üretiliyor...");
                    planContent = await _aiPlanGenerator.GeneratePlanAsync(objective, context ?? "", cancellationToken);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _terminalLog($"⚠️ AI plan üretimi başarısız, fallback kullanılıyor: {ex.Message}");
                    planContent = GenerateFallbackPlan(objective, context);
                }
            }
            else
            {
                _terminalLog("📋 Template fallback plan kullanılıyor (AI yok)");
                planContent = GenerateFallbackPlan(objective, context);
            }

            var planJson = new
            {
                Id = planId,
                Objective = objective,
                Context = context,
                Files = Directory.Exists(_projectFolder)
                    ? Directory.GetFiles(_projectFolder!, "*.cs", SearchOption.AllDirectories)
                        .Take(20)
                        .Select(Path.GetFileName)
                        .ToList()
                    : new List<string?>(),
                CreatedAt = DateTime.Now.ToString("O"),
                Status = "Created",
                Content = planContent
            };

            cancellationToken.ThrowIfCancellationRequested();
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
            await File.WriteAllTextAsync(planFile, JsonSerializer.Serialize(planJson, jsonOptions), cancellationToken);

            var memoryDirectory = Path.Combine(_projectFolder ?? ".", ".mdai");
            Directory.CreateDirectory(memoryDirectory);
            var memory = LoadMemoryDocument();
            memory.LastTask = objective;
            memory.LastPlan = planFile;
            SaveMemoryDocument(memory);

            _terminalLog($"✓ Plan oluşturuldu: {planFile}");
            EventBus.Publish(new TimelineEvent { Type = TimelineEventType.Thinking, IsCompleted = true, Message = "Plan oluşturuldu." });
            return new ToolResult
            {
                Success = true,
                Output = JsonSerializer.Serialize(new
                {
                    planId,
                    objective,
                    plan = planContent,
                    filePath = planFile,
                    files = planJson.Files
                })
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            EventBus.Publish(new TimelineEvent { Type = TimelineEventType.Failed, IsFailed = true, Message = "Plan oluşturulamadı." });
            return new ToolResult { Success = false, Error = $"Plan oluşturma hatası: {ex.Message}" };
        }
    }

    private string GenerateFallbackPlan(string objective, string? context)
    {
        return $"""
            # Plan: {objective}

            ## Adımlar

            1. **Gereksinimleri Analiz Et**
               - Görevin kapsamını belirle
               - Gerekli dosyaları ve araçları tanımla

            2. **Teknik Mimariye Karar Ver**
               - Teknoloji stack'ini seç
               - Mimari pattern'leri planla

            3. **Uygulama Tasarımı**
               - Modülleri tasarla
               - API/interface'leri tanımla

            4. **Implementasyon**
               - Core fonksiyonları yap
               - Testleri ekle

            5. **Doğrulama ve İyileştirme**
               - Tüm gereksinimler karşılanmış mı kontrol et
               - Performans ve güvenlik iyileştirmeleri yap

            6. **Finalizasyon**
               - Dokumentasyon tamamla
               - Final test ve deployment
            """;
    }

    public async Task<ToolResult> GenerateRecoveryPlanAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var objective = arguments.TryGetProperty("objective", out var objProp) ? objProp.GetString() : arguments.TryGetProperty("task", out var tProp) ? tProp.GetString() : "Kurtarma";
            var error = arguments.TryGetProperty("error", out var errProp) ? errProp.GetString() : arguments.TryGetProperty("lastError", out var leProp) ? leProp.GetString() : "";
            var context = arguments.TryGetProperty("context", out var ctxProp) ? ctxProp.GetString() : "";

            if (string.IsNullOrWhiteSpace(objective) || string.IsNullOrWhiteSpace(error))
                return new ToolResult { Success = false, Error = "objective (veya task) ve error (veya lastError) zorunlu." };

            _terminalLog("🔧 Hata iyileşme planı üretiliyor...");
            cancellationToken.ThrowIfCancellationRequested();
            var recoveryPlan = _aiPlanGenerator != null
                ? await _aiPlanGenerator.GenerateRecoveryPlanAsync(objective, error, context, cancellationToken)
                : $"# Recovery Plan\n\nGörev: {objective}\nHata: {error}\n\nHata analiz edilip düzeltme uygulanmalıdır.";

            var planId = Guid.NewGuid().ToString()[..8];
            var plansDir = Path.Combine(_projectFolder ?? ".", ".mdai", "plans");
            Directory.CreateDirectory(plansDir);

            var planFilePath = Path.Combine(plansDir, $"recovery_plan_{planId}.json");
            var planJson = new
            {
                Id = planId,
                Type = "recovery",
                Objective = objective,
                Error = error,
                Content = recoveryPlan,
                CreatedAt = DateTime.UtcNow.ToString("O")
            };

            cancellationToken.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(planFilePath, JsonSerializer.Serialize(planJson, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

            return new ToolResult
            {
                Success = true,
                Output = JsonSerializer.Serialize(new { planId, recoveryPlan, filePath = planFilePath })
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new ToolResult { Success = false, Error = $"Hata iyileşme planı hatası: {ex.Message}" };
        }
    }

    public async Task<ToolResult> GenerateTaskGraphAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var objective = arguments.TryGetProperty("objective", out var objProp) ? objProp.GetString() : arguments.TryGetProperty("task", out var tProp) ? tProp.GetString() : "";
            var context = arguments.TryGetProperty("context", out var ctxProp) ? ctxProp.GetString() : "";

            if (string.IsNullOrWhiteSpace(objective))
                return new ToolResult { Success = false, Error = "objective (veya task) zorunlu." };

            _terminalLog("🔗 Görev grafiği üretiliyor...");
            cancellationToken.ThrowIfCancellationRequested();
            var taskGraph = _aiPlanGenerator != null
                ? await _aiPlanGenerator.GenerateTaskGraphAsync(objective, context, cancellationToken)
                : $"Task: {objective}";

            return new ToolResult
            {
                Success = true,
                Output = JsonSerializer.Serialize(new { taskGraph })
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new ToolResult { Success = false, Error = $"Görev grafiği hatası: {ex.Message}" };
        }
    }

    public async Task<ToolResult> GenerateDiffAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var oldPath = arguments.TryGetProperty("oldPath", out var oProp) ? oProp.GetString() : arguments.TryGetProperty("filePath", out var fProp) ? fProp.GetString() : "";
        var newPath = arguments.TryGetProperty("newPath", out var nProp) ? nProp.GetString() : oldPath;
        var newContentArg = arguments.TryGetProperty("newContent", out var ncProp) ? ncProp.GetString() : null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var oldContent = File.Exists(oldPath) ? await File.ReadAllTextAsync(oldPath, cancellationToken) : "";
            var newContent = newContentArg ?? (File.Exists(newPath) ? await File.ReadAllTextAsync(newPath, cancellationToken) : "");

            var oldLines = oldContent.Split('\n');
            var newLines = newContent.Split('\n');

            var diff = new List<string> { $"--- {oldPath}", $"+++ {newPath}" };

            for (int i = 0; i < Math.Max(oldLines.Length, newLines.Length); i++)
            {
                var oldL = i < oldLines.Length ? oldLines[i] : "";
                var neuL = i < newLines.Length ? newLines[i] : "";

                if (oldL != neuL)
                {
                    if (oldL.Length > 0) diff.Add($"- {oldL}");
                    if (neuL.Length > 0) diff.Add($"+ {neuL}");
                }
            }

            var output = _optimizer.OptimizeTerminalOutput(string.Join("\n", diff));
            return new ToolResult { Success = true, Output = output };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new ToolResult { Success = false, Error = $"Diff oluşturma hatası: {ex.Message}" };
        }
    }

    public async Task<ToolResult> ReadProjectMemoryAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = arguments.TryGetProperty("key", out var keyProp) ? keyProp.GetString() : null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var memoryFile = GetMemoryFilePath();
            if (!File.Exists(memoryFile))
                return new ToolResult { Success = true, Output = "Proje belleği boş." };

            var json = await File.ReadAllTextAsync(memoryFile, cancellationToken);
            var document = JsonSerializer.Deserialize<ProjectMemoryDocument>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new ProjectMemoryDocument();
            if (string.IsNullOrWhiteSpace(key))
            {
                return new ToolResult
                {
                    Success = true,
                    Output = JsonSerializer.Serialize(document, new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    })
                };
            }

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["lastTask"] = document.LastTask,
                ["lastPlan"] = document.LastPlan
            };
            foreach (var entry in document.Architecture) values[$"architecture.{entry.Key}"] = entry.Value;
            foreach (var entry in document.Conventions) values[$"conventions.{entry.Key}"] = entry.Value;

            return values.TryGetValue(key, out var value)
                ? new ToolResult { Success = true, Output = value }
                : new ToolResult { Success = false, Error = $"Hafıza anahtarı bulunamadı: {key}" };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new ToolResult { Success = false, Error = $"Bellek okuma hatası: {ex.Message}" };
        }
    }

    public async Task<ToolResult> WriteProjectMemoryAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var category = arguments.TryGetProperty("category", out var categoryProp) ? categoryProp.GetString() : null;
        var key = arguments.TryGetProperty("key", out var keyProp) ? keyProp.GetString() : null;
        var value = arguments.TryGetProperty("value", out var valueProp) ? valueProp.GetString() : null;

        if (category is not ("architecture" or "conventions") || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            return new ToolResult { Success = false, Error = "category (architecture/conventions), key ve value zorunludur." };

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var memoryFile = GetMemoryFilePath();
            var document = File.Exists(memoryFile)
                ? JsonSerializer.Deserialize<ProjectMemoryDocument>(
                    await File.ReadAllTextAsync(memoryFile, cancellationToken),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new ProjectMemoryDocument()
                : new ProjectMemoryDocument();
            var target = category.Equals("architecture", StringComparison.OrdinalIgnoreCase)
                ? document.Architecture
                : document.Conventions;
            target[key.Trim()] = value.Trim();
            document.Archived.Remove($"{category}:{key.Trim()}");

            document.SchemaVersion = CurrentMemorySchemaVersion;
            document.UpdatedAt = DateTime.UtcNow.ToString("O");
            var memoryDirectory = Path.GetDirectoryName(GetMemoryFilePath())!;
            Directory.CreateDirectory(memoryDirectory);
            cancellationToken.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(
                GetMemoryFilePath(),
                JsonSerializer.Serialize(document, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }), cancellationToken);
            return new ToolResult { Success = true, Output = $"Hafıza kaydedildi: {category}.{key}" };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new ToolResult { Success = false, Error = $"Hafıza yazma hatası: {ex.Message}" };
        }
    }

    public async Task<ToolResult> SearchProjectMemoryAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var query = arguments.TryGetProperty("query", out var queryProp) ? queryProp.GetString() : null;
        if (string.IsNullOrWhiteSpace(query))
            return new ToolResult { Success = false, Error = "query zorunludur." };

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var memoryFile = GetMemoryFilePath();
            var document = File.Exists(memoryFile)
                ? JsonSerializer.Deserialize<ProjectMemoryDocument>(
                    await File.ReadAllTextAsync(memoryFile, cancellationToken),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new ProjectMemoryDocument()
                : new ProjectMemoryDocument();
            var results = new List<MemorySearchResult>();
            foreach (var entry in document.Architecture.Where(entry => Matches(entry.Key, entry.Value, query)))
                results.Add(new MemorySearchResult(Category: "architecture", Key: entry.Key, Value: entry.Value));
            foreach (var entry in document.Conventions.Where(entry => Matches(entry.Key, entry.Value, query)))
                results.Add(new MemorySearchResult(Category: "conventions", Key: entry.Key, Value: entry.Value));

            return new ToolResult
            {
                Success = true,
                Output = JsonSerializer.Serialize(new { query, count = results.Count, results })
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new ToolResult { Success = false, Error = $"Bellek arama hatası: {ex.Message}" };
        }
    }

    public async Task<ToolResult> ArchiveProjectMemoryAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var category = arguments.TryGetProperty("category", out var categoryProp) ? categoryProp.GetString() : null;
        var key = arguments.TryGetProperty("key", out var keyProp) ? keyProp.GetString() : null;
        if (category is not ("architecture" or "conventions") || string.IsNullOrWhiteSpace(key))
            return new ToolResult { Success = false, Error = "category (architecture/conventions) ve key zorunludur." };

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var memoryFile = GetMemoryFilePath();
            var document = File.Exists(memoryFile)
                ? JsonSerializer.Deserialize<ProjectMemoryDocument>(
                    await File.ReadAllTextAsync(memoryFile, cancellationToken),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new ProjectMemoryDocument()
                : new ProjectMemoryDocument();
            var source = category.Equals("architecture", StringComparison.OrdinalIgnoreCase)
                ? document.Architecture
                : document.Conventions;
            if (!source.Remove(key, out var value))
                return new ToolResult { Success = false, Error = $"Arşivlenecek hafıza bulunamadı: {category}.{key}" };

            document.Archived[$"{category}:{key}"] = value;

            document.SchemaVersion = CurrentMemorySchemaVersion;
            document.UpdatedAt = DateTime.UtcNow.ToString("O");
            var memoryDirectory = Path.GetDirectoryName(GetMemoryFilePath())!;
            Directory.CreateDirectory(memoryDirectory);
            cancellationToken.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(
                GetMemoryFilePath(),
                JsonSerializer.Serialize(document, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }), cancellationToken);
            return new ToolResult { Success = true, Output = $"Hafıza arşivlendi: {category}.{key}" };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new ToolResult { Success = false, Error = $"Bellek arşivleme hatası: {ex.Message}" };
        }
    }

    private static bool Matches(string key, string value, string query) =>
        key.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        value.Contains(query, StringComparison.OrdinalIgnoreCase);

    // ============================================================
    // [FAZ 2] WriteDecisionAsync — Kalıcı Mimari Karar Kaydı
    // ============================================================
    /// <summary>
    /// Önemli bir mimari kararı .mdai/decisions.json dosyasına ekler.
    /// Dosya yoksa oluşturulur; varsa mevcut listeye eklenir.
    /// </summary>
    public async Task<ToolResult> WriteDecisionAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var topic    = arguments.TryGetProperty("topic",    out var t) ? t.GetString() ?? "" : "";
            var decision = arguments.TryGetProperty("decision", out var d) ? d.GetString() ?? "" : "";
            var reason   = arguments.TryGetProperty("reason",   out var r) ? r.GetString() ?? "" : "";

            if (string.IsNullOrWhiteSpace(topic) || string.IsNullOrWhiteSpace(decision))
                return new ToolResult { Success = false, Error = "topic ve decision alanları zorunludur." };

            var mdaiDir = Path.Combine(_projectFolder ?? ".", ".mdai");
            Directory.CreateDirectory(mdaiDir);

            var decisionsFile = Path.Combine(mdaiDir, "decisions.json");

            cancellationToken.ThrowIfCancellationRequested();
            List<DecisionRecord> decisions;
            if (File.Exists(decisionsFile))
            {
                var existing = await File.ReadAllTextAsync(decisionsFile, cancellationToken);
                try
                {
                    decisions = JsonSerializer.Deserialize<List<DecisionRecord>>(existing, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                                ?? new List<DecisionRecord>();
                }
                catch (JsonException)
                {
                    // Eski format (anonim nesneler) veya bozuk JSON: bozuk kayıtları sessizce korumak yerine
                    // yeni tip güvenli formatla sıfırdan başla; veri kaybı olmasın diye yedek al
                    try
                    {
                        var backupFile = Path.Combine(mdaiDir, $"decisions.legacy_{DateTime.Now:yyyyMMdd_HHmmss}.bak.json");
                        await File.WriteAllTextAsync(backupFile, existing, cancellationToken);
                        _terminalLog?.Invoke($"⚠️ decisions.json bozuk veya eski format; yedek alındı: {Path.GetFileName(backupFile)}");
                    }
                    catch { /* yedek başarısız olursa devam et */ }
                    decisions = new List<DecisionRecord>();
                }
            }
            else
            {
                decisions = new List<DecisionRecord>();
            }

            var newEntry = new DecisionRecord(
                Date:     DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                Topic:    topic,
                Decision: decision,
                Reason:   reason
            );
            decisions.Add(newEntry);

            cancellationToken.ThrowIfCancellationRequested();
            var json = JsonSerializer.Serialize(decisions, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(decisionsFile, json, cancellationToken);

            return new ToolResult
            {
                Success = true,
                Output  = $"✅ Karar kaydedildi → .mdai/decisions.json\nKonu: {topic}\nKarar: {decision}"
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new ToolResult { Success = false, Error = $"Karar yazma hatası: {ex.Message}" };
        }
    }

    // ============================================================
    // [FAZ 2] WriteTaskAsync — Kalıcı Görev Takibi
    // ============================================================
    /// <summary>
    /// Bir görevi .mdai/tasks/{taskId}.md dosyasına yazar.
    /// Her taskId için ayrı bir dosya oluşturulur/güncellenir.
    /// </summary>
    public async Task<ToolResult> WriteTaskAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var taskId      = arguments.TryGetProperty("taskId",      out var id)   ? id.GetString()   ?? "" : "";
            var title       = arguments.TryGetProperty("title",       out var ti)   ? ti.GetString()   ?? "" : "";
            var status      = arguments.TryGetProperty("status",      out var st)   ? st.GetString()   ?? "in-progress" : "in-progress";
            var description = arguments.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "";

            if (string.IsNullOrWhiteSpace(taskId) || string.IsNullOrWhiteSpace(title))
                return new ToolResult { Success = false, Error = "taskId ve title alanları zorunludur." };

            var safeId = System.Text.RegularExpressions.Regex.Replace(taskId.Trim().ToLowerInvariant(), @"[^\w\-]", "-");

            var tasksDir = Path.Combine(_projectFolder ?? ".", ".mdai", "tasks");
            Directory.CreateDirectory(tasksDir);

            var taskFile = Path.Combine(tasksDir, $"{safeId}.md");

            var statusEmoji = status.ToLower() switch
            {
                "completed" => "✅",
                "blocked"   => "🚫",
                _           => "🔄"
            };

            var content = $"""
                # {statusEmoji} {title}

                **ID:** {safeId}
                **Durum:** {status}
                **Son Güncelleme:** {DateTime.Now:yyyy-MM-dd HH:mm}

                ## Detaylar
                {description}
                """;

            cancellationToken.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(taskFile, content, cancellationToken);

            return new ToolResult
            {
                Success = true,
                Output  = $"✅ Görev kaydedildi → .mdai/tasks/{safeId}.md\nDurum: {statusEmoji} {status}"
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new ToolResult { Success = false, Error = $"Görev yazma hatası: {ex.Message}" };
        }
    }
}

