using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Globalization;
using System.Linq;

namespace mdaiAgent
{
    public class ModelPricing
    {
        public string Id { get; set; } = "";
        public decimal PromptPricePerToken { get; set; }
        public decimal CompletionPricePerToken { get; set; }
    }

    public class TokenStatsFile
    {
        [JsonPropertyName("totalTokens")]
        public int TotalTokens { get; set; }

        [JsonPropertyName("promptTokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("completionTokens")]
        public int CompletionTokens { get; set; }

        [JsonPropertyName("totalCost")]
        public decimal TotalCost { get; set; }

        [JsonPropertyName("lastUpdated")]
        public string LastUpdated { get; set; } = "";
    }

    // ============================================================
    // [FAZ 4] Araç (Tool) Telemetrisi
    // ============================================================
    public class ToolTelemetryStats
    {
        public int TotalCalls { get; set; }
        public int SuccessfulCalls { get; set; }
        public int FailedCalls { get; set; }
        public long TotalDurationMs { get; set; }
        public double AverageDurationMs => TotalCalls > 0 ? (double)TotalDurationMs / TotalCalls : 0;
    }

    public class VerificationTelemetryStats
    {
        public int TotalRuns { get; set; }
        public int SuccessfulRuns { get; set; }
        public int FailedRuns { get; set; }
        public long TotalDurationMs { get; set; }
        public string LastStatus { get; set; } = "";
        public string LastUpdated { get; set; } = "";
        public double AverageDurationMs => TotalRuns > 0 ? (double)TotalDurationMs / TotalRuns : 0;
    }

    public class ModelTelemetryStats
    {
        public int TotalRequests { get; set; }
        public int SuccessfulRequests { get; set; }
        public int FailedRequests { get; set; }
        public int RetryRequests { get; set; }
        public long TotalLatencyMs { get; set; }
        public double AverageLatencyMs => TotalRequests > 0 ? (double)TotalLatencyMs / TotalRequests : 0;
    }

    public class RouterTelemetryStats
    {
        public int TotalRoutes { get; set; }
        public int SuccessfulSelections { get; set; }
        public int FallbackRoutes { get; set; }
        public int TimeoutRoutes { get; set; }
        public int ErrorRoutes { get; set; }
        public int LowConfidenceRoutes { get; set; }
        public int InvalidSelectionRoutes { get; set; }
        public int EmptyToolSelections { get; set; }
        public long TotalDurationMs { get; set; }
        public string LastStatus { get; set; } = "";
        public string LastReason { get; set; } = "";
        public string LastUpdated { get; set; } = "";
        public double AverageDurationMs => TotalRoutes > 0 ? (double)TotalDurationMs / TotalRoutes : 0;
        public double FallbackRate => TotalRoutes == 0 ? 0 : FallbackRoutes * 100.0 / TotalRoutes;
    }

    public class SessionTelemetryRecord
    {
        public string StartedAt { get; set; } = "";
        public string EndedAt { get; set; } = "";
        public int ModelRequests { get; set; }
        public int ProviderFailures { get; set; }
        public int ToolCalls { get; set; }
        public int VerificationRuns { get; set; }
    }

    public class CentralTelemetrySummary
    {
        public int TotalModelRequests { get; set; }
        public int SuccessfulModelRequests { get; set; }
        public int FailedModelRequests { get; set; }
        public int RetryRequests { get; set; }
        public int ProviderFailures { get; set; }
        public int TotalToolCalls { get; set; }
        public int TotalVerificationRuns { get; set; }
        public int SuccessfulVerificationRuns { get; set; }
        public int FailedVerificationRuns { get; set; }
        public int TotalSessions { get; set; }
        public string CurrentSessionStartedAt { get; set; } = "";
        public List<SessionTelemetryRecord> RecentSessions { get; set; } = new();
        public double ModelSuccessRate => TotalModelRequests == 0 ? 0 : SuccessfulModelRequests * 100.0 / TotalModelRequests;
        public double VerificationSuccessRate => TotalVerificationRuns == 0 ? 0 : SuccessfulVerificationRuns * 100.0 / TotalVerificationRuns;
    }

    public class TokenTrackerService

    {
        private static TokenTrackerService? _instance;
        public static TokenTrackerService Instance => _instance ??= new TokenTrackerService();

        private Dictionary<string, ModelPricing> _pricingCache = new();
        private readonly HttpClient _httpClient = new();

        // Aktif proje klasörü
        private string? _projectFolder;

        public int CurrentSessionTokens { get; private set; }
        public int CurrentSessionPromptTokens { get; private set; }
        public int CurrentSessionCompletionTokens { get; private set; }
        public decimal CurrentSessionCost { get; private set; }
        public int TotalProjectTokens { get; private set; }
        public int TotalProjectPromptTokens { get; private set; }
        public int TotalProjectCompletionTokens { get; private set; }
        public decimal TotalProjectCost { get; private set; }

        public event Action<int, decimal, int, decimal>? OnUsageUpdated;

        private TokenTrackerService()
        {
            _ = FetchPricingAsync();
        }

        /// <summary>
        /// Proje değiştiğinde çağırın: eski projeyi kaydeder, yeni projenin istatistiklerini yükler.
        /// </summary>
        public void SetProject(string? projectFolder)
        {
            FinalizeCurrentSession();
            SaveProjectStats();                  // eski projeyi kaydet
            _projectFolder = projectFolder;
            LoadProjectStats();                  // yeni projeyi yükle
            LoadTelemetryStats();                // [FAZ 4] telemetriyi yükle
            LoadVerificationTelemetry();
            LoadModelTelemetry();
            LoadRouterTelemetry();
            LoadCentralTelemetry();
            _currentSession = new SessionTelemetryRecord { StartedAt = DateTime.UtcNow.ToString("O") };
            CurrentSessionTokens = 0;            // oturum sıfırla
            CurrentSessionPromptTokens = 0;
            CurrentSessionCompletionTokens = 0;
            CurrentSessionCost = 0;
            OnUsageUpdated?.Invoke(CurrentSessionTokens, CurrentSessionCost, TotalProjectTokens, TotalProjectCost);
        }

        private void LoadProjectStats()
        {
            TotalProjectTokens = 0;
            TotalProjectPromptTokens = 0;
            TotalProjectCompletionTokens = 0;
            TotalProjectCost = 0;
            var path = GetStatsFilePath();
            if (path == null || !File.Exists(path)) return;
                try
            {
                var json = File.ReadAllText(path);
                var stats = JsonSerializer.Deserialize<TokenStatsFile>(json);
                if (stats != null)
                {
                    TotalProjectTokens = stats.TotalTokens;
                    TotalProjectPromptTokens = stats.PromptTokens;
                    TotalProjectCompletionTokens = stats.CompletionTokens;
                    TotalProjectCost = stats.TotalCost;
                }
            }
            catch (Exception ex) { Logger.LogError($"Token stats load failed: {ex.Message}"); }
        }

        private void SaveProjectStats()
        {
            var path = GetStatsFilePath();
            if (path == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var stats = new TokenStatsFile
                {
                    TotalTokens = TotalProjectTokens,
                    PromptTokens = TotalProjectPromptTokens,
                    CompletionTokens = TotalProjectCompletionTokens,
                    TotalCost = TotalProjectCost,
                    LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };
                File.WriteAllText(path, JsonSerializer.Serialize(stats, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { Logger.LogError($"Token stats save failed: {ex.Message}"); }
        }

        private string? GetStatsFilePath()
        {
            if (string.IsNullOrEmpty(_projectFolder)) return null;
            return Path.Combine(_projectFolder, ".mdai", "token_stats.json");
        }

        public async Task FetchPricingAsync()
        {
            try
            {
                var response = await _httpClient.GetStringAsync("https://openrouter.ai/api/v1/models");
                using var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var model in dataElement.EnumerateArray())
                    {
                        var id = model.GetProperty("id").GetString();
                        if (string.IsNullOrEmpty(id)) continue;
                        decimal promptPrice = 0, completionPrice = 0;
                        if (model.TryGetProperty("pricing", out var pricingElement))
                        {
                            if (pricingElement.TryGetProperty("prompt", out var pEl) && decimal.TryParse(pEl.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var p))
                                promptPrice = p;
                            if (pricingElement.TryGetProperty("completion", out var cEl) && decimal.TryParse(cEl.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var c))
                                completionPrice = c;
                        }
                        _pricingCache[id] = new ModelPricing { Id = id, PromptPricePerToken = promptPrice, CompletionPricePerToken = completionPrice };
                    }
                }
            }
            catch (Exception ex) { Logger.LogError($"Pricing fetch failed: {ex.Message}"); }
        }

        public void AddUsage(string modelId, ChatUsage usage)
        {
            if (usage == null || usage.TotalTokens == 0) return;

            decimal cost = 0;
            if (_pricingCache.TryGetValue(modelId, out var pricing))
                cost = (usage.PromptTokens * pricing.PromptPricePerToken) + (usage.CompletionTokens * pricing.CompletionPricePerToken);

            CurrentSessionTokens += usage.TotalTokens;
            CurrentSessionPromptTokens += usage.PromptTokens;
            CurrentSessionCompletionTokens += usage.CompletionTokens;
            CurrentSessionCost += cost;
            TotalProjectTokens += usage.TotalTokens;
            TotalProjectPromptTokens += usage.PromptTokens;
            TotalProjectCompletionTokens += usage.CompletionTokens;
            TotalProjectCost += cost;

            SaveProjectStats();   // her kullanımdan sonra diske yaz
            OnUsageUpdated?.Invoke(CurrentSessionTokens, CurrentSessionCost, TotalProjectTokens, TotalProjectCost);
        }

        public void ResetSession()
        {
            CurrentSessionTokens = 0;
            CurrentSessionPromptTokens = 0;
            CurrentSessionCompletionTokens = 0;
            CurrentSessionCost = 0;
            OnUsageUpdated?.Invoke(CurrentSessionTokens, CurrentSessionCost, TotalProjectTokens, TotalProjectCost);
        }

        // ============================================================
        // [FAZ 4] LogToolExecution — Araç Telemetrisini Kaydet
        // ============================================================
        private Dictionary<string, ToolTelemetryStats> _toolTelemetry = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, ModelTelemetryStats> _modelTelemetry = new(StringComparer.OrdinalIgnoreCase);
        private RouterTelemetryStats _routerTelemetry = new();
        private VerificationTelemetryStats _verificationTelemetry = new();
        private CentralTelemetrySummary _centralTelemetry = new();
        private SessionTelemetryRecord _currentSession = new() { StartedAt = DateTime.UtcNow.ToString("O") };
        private const int MaxRecentSessions = 100;

        private void LoadTelemetryStats()
        {
            _toolTelemetry.Clear();
            if (string.IsNullOrEmpty(_projectFolder)) return;
            var path = Path.Combine(_projectFolder, ".mdai", "tool_telemetry.json");
            if (!File.Exists(path)) return;
            
            try
            {
                var json = File.ReadAllText(path);
                var data = JsonSerializer.Deserialize<Dictionary<string, ToolTelemetryStats>>(json);
                if (data != null) _toolTelemetry = data;
            }
            catch { }
        }

        private void SaveTelemetryStats()
        {
            if (string.IsNullOrEmpty(_projectFolder)) return;
            var path = Path.Combine(_projectFolder, ".mdai", "tool_telemetry.json");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var json = JsonSerializer.Serialize(_toolTelemetry, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch { }
        }

        public void LogToolExecution(string toolName, bool success, long durationMs)
        {
            if (string.IsNullOrEmpty(toolName)) return;

            lock (_toolTelemetry)
            {
                if (!_toolTelemetry.TryGetValue(toolName, out var stats))
                {
                    stats = new ToolTelemetryStats();
                    _toolTelemetry[toolName] = stats;
                }

                stats.TotalCalls++;
                stats.TotalDurationMs += durationMs;
                
                if (success) stats.SuccessfulCalls++;
                else stats.FailedCalls++;
                _centralTelemetry.TotalToolCalls++;
                _currentSession.ToolCalls++;

                // Her çağrıda yazmak I/O yükü getirebilir ama güvenlik için şimdilik yazıyoruz
                // İleride debounce / periyodik kaydetme eklenebilir.
                SaveTelemetryStats();
                SaveCentralTelemetry();
            }
        }

        private void LoadVerificationTelemetry()
        {
            _verificationTelemetry = new VerificationTelemetryStats();
            if (string.IsNullOrEmpty(_projectFolder)) return;

            var path = Path.Combine(_projectFolder, ".mdai", "verification_telemetry.json");
            if (!File.Exists(path)) return;

            try
            {
                var json = File.ReadAllText(path);
                _verificationTelemetry = JsonSerializer.Deserialize<VerificationTelemetryStats>(json) ?? new VerificationTelemetryStats();
            }
            catch { }
        }

        private void SaveVerificationTelemetry()
        {
            if (string.IsNullOrEmpty(_projectFolder)) return;

            try
            {
                var path = Path.Combine(_projectFolder, ".mdai", "verification_telemetry.json");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(_verificationTelemetry, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        public VerificationTelemetryStats GetVerificationTelemetry()
        {
            return new VerificationTelemetryStats
            {
                TotalRuns = _verificationTelemetry.TotalRuns,
                SuccessfulRuns = _verificationTelemetry.SuccessfulRuns,
                FailedRuns = _verificationTelemetry.FailedRuns,
                TotalDurationMs = _verificationTelemetry.TotalDurationMs,
                LastStatus = _verificationTelemetry.LastStatus,
                LastUpdated = _verificationTelemetry.LastUpdated
            };
        }

        public void RecordVerification(VerificationResult result, long durationMs)
        {
            if (result == null) return;

            _verificationTelemetry.TotalRuns++;
            _verificationTelemetry.TotalDurationMs += Math.Max(0, durationMs);
            if (result.Success)
                _verificationTelemetry.SuccessfulRuns++;
            else
                _verificationTelemetry.FailedRuns++;

            _verificationTelemetry.LastStatus = result.Status;
            _verificationTelemetry.LastUpdated = DateTime.Now.ToString("O");
            _centralTelemetry.TotalVerificationRuns++;
            _currentSession.VerificationRuns++;
            if (result.Success)
                _centralTelemetry.SuccessfulVerificationRuns++;
            else
                _centralTelemetry.FailedVerificationRuns++;
            SaveVerificationTelemetry();
            SaveCentralTelemetry();
        }

        private void LoadModelTelemetry()
        {
            _modelTelemetry.Clear();
            if (string.IsNullOrEmpty(_projectFolder)) return;

            var path = Path.Combine(_projectFolder, ".mdai", "model_telemetry.json");
            if (!File.Exists(path)) return;

            try
            {
                var json = File.ReadAllText(path);
                var data = JsonSerializer.Deserialize<Dictionary<string, ModelTelemetryStats>>(json);
                if (data != null) _modelTelemetry = data;
            }
            catch { }
        }

        private void SaveModelTelemetry()
        {
            if (string.IsNullOrEmpty(_projectFolder)) return;

            try
            {
                var path = Path.Combine(_projectFolder, ".mdai", "model_telemetry.json");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(_modelTelemetry, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        private void LoadRouterTelemetry()
        {
            _routerTelemetry = new RouterTelemetryStats();
            if (string.IsNullOrEmpty(_projectFolder)) return;

            var path = Path.Combine(_projectFolder, ".mdai", "router_telemetry.json");
            if (!File.Exists(path)) return;

            try
            {
                var json = File.ReadAllText(path);
                _routerTelemetry = JsonSerializer.Deserialize<RouterTelemetryStats>(json) ?? new RouterTelemetryStats();
            }
            catch { }
        }

        private void SaveRouterTelemetry()
        {
            if (string.IsNullOrEmpty(_projectFolder)) return;

            try
            {
                var path = Path.Combine(_projectFolder, ".mdai", "router_telemetry.json");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(_routerTelemetry, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        public RouterTelemetryStats GetRouterTelemetrySnapshot()
        {
            lock (_routerTelemetry)
            {
                return new RouterTelemetryStats
                {
                    TotalRoutes = _routerTelemetry.TotalRoutes,
                    SuccessfulSelections = _routerTelemetry.SuccessfulSelections,
                    FallbackRoutes = _routerTelemetry.FallbackRoutes,
                    TimeoutRoutes = _routerTelemetry.TimeoutRoutes,
                    ErrorRoutes = _routerTelemetry.ErrorRoutes,
                    LowConfidenceRoutes = _routerTelemetry.LowConfidenceRoutes,
                    InvalidSelectionRoutes = _routerTelemetry.InvalidSelectionRoutes,
                    EmptyToolSelections = _routerTelemetry.EmptyToolSelections,
                    TotalDurationMs = _routerTelemetry.TotalDurationMs,
                    LastStatus = _routerTelemetry.LastStatus,
                    LastReason = _routerTelemetry.LastReason,
                    LastUpdated = _routerTelemetry.LastUpdated
                };
            }
        }

        public void RecordRouterDecision(string status, string reason, double confidence, long durationMs)
        {
            if (string.IsNullOrWhiteSpace(status)) return;

            lock (_routerTelemetry)
            {
                _routerTelemetry.TotalRoutes++;
                _routerTelemetry.TotalDurationMs += Math.Max(0, durationMs);
                _routerTelemetry.LastStatus = status;
                _routerTelemetry.LastReason = reason ?? "";
                _routerTelemetry.LastUpdated = DateTime.UtcNow.ToString("O");

                switch (status)
                {
                    case "selected":
                        _routerTelemetry.SuccessfulSelections++;
                        break;
                    case "empty-selection":
                        _routerTelemetry.SuccessfulSelections++;
                        _routerTelemetry.EmptyToolSelections++;
                        break;
                    case "fallback-timeout":
                        _routerTelemetry.FallbackRoutes++;
                        _routerTelemetry.TimeoutRoutes++;
                        break;
                    case "fallback-error":
                        _routerTelemetry.FallbackRoutes++;
                        _routerTelemetry.ErrorRoutes++;
                        break;
                    case "fallback-low-confidence":
                        _routerTelemetry.FallbackRoutes++;
                        _routerTelemetry.LowConfidenceRoutes++;
                        break;
                    case "fallback-invalid-selection":
                        _routerTelemetry.FallbackRoutes++;
                        _routerTelemetry.InvalidSelectionRoutes++;
                        break;
                    default:
                        _routerTelemetry.FallbackRoutes++;
                        break;
                }

                SaveRouterTelemetry();
            }
        }

        private void LoadCentralTelemetry()
        {
            _centralTelemetry = new CentralTelemetrySummary();
            if (string.IsNullOrEmpty(_projectFolder)) return;

            var path = Path.Combine(_projectFolder, ".mdai", "telemetry_summary.json");
            if (!File.Exists(path)) return;

            try
            {
                var json = File.ReadAllText(path);
                _centralTelemetry = JsonSerializer.Deserialize<CentralTelemetrySummary>(json) ?? new CentralTelemetrySummary();
                _centralTelemetry.RecentSessions = _centralTelemetry.RecentSessions.TakeLast(MaxRecentSessions).ToList();
            }
            catch { }
        }

        private void SaveCentralTelemetry()
        {
            if (string.IsNullOrEmpty(_projectFolder)) return;

            try
            {
                var path = Path.Combine(_projectFolder, ".mdai", "telemetry_summary.json");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(_centralTelemetry, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        private void FinalizeCurrentSession()
        {
            if (string.IsNullOrEmpty(_projectFolder)) return;
            if (_currentSession.ModelRequests == 0 && _currentSession.ToolCalls == 0 && _currentSession.VerificationRuns == 0) return;

            _currentSession.EndedAt = DateTime.UtcNow.ToString("O");
            _centralTelemetry.RecentSessions.Add(_currentSession);
            _centralTelemetry.RecentSessions = _centralTelemetry.RecentSessions.TakeLast(MaxRecentSessions).ToList();
            _centralTelemetry.TotalSessions++;
            SaveCentralTelemetry();
        }

        public CentralTelemetrySummary GetCentralTelemetrySnapshot()
        {
            lock (_centralTelemetry)
            {
                return new CentralTelemetrySummary
                {
                    TotalModelRequests = _centralTelemetry.TotalModelRequests,
                    SuccessfulModelRequests = _centralTelemetry.SuccessfulModelRequests,
                    FailedModelRequests = _centralTelemetry.FailedModelRequests,
                    RetryRequests = _centralTelemetry.RetryRequests,
                    ProviderFailures = _centralTelemetry.ProviderFailures,
                    TotalToolCalls = _centralTelemetry.TotalToolCalls,
                    TotalVerificationRuns = _centralTelemetry.TotalVerificationRuns,
                    SuccessfulVerificationRuns = _centralTelemetry.SuccessfulVerificationRuns,
                    FailedVerificationRuns = _centralTelemetry.FailedVerificationRuns,
                    TotalSessions = _centralTelemetry.TotalSessions,
                    CurrentSessionStartedAt = _currentSession.StartedAt,
                    RecentSessions = _centralTelemetry.RecentSessions.ToList()
                };
            }
        }

        public IReadOnlyDictionary<string, ModelTelemetryStats> GetModelTelemetrySnapshot()
        {
            lock (_modelTelemetry)
            {
                return new Dictionary<string, ModelTelemetryStats>(_modelTelemetry, StringComparer.OrdinalIgnoreCase);
            }
        }

        public void RecordModelRequest(string modelName, bool success, long latencyMs, bool isRetry = false)
        {
            if (string.IsNullOrWhiteSpace(modelName)) return;

            lock (_modelTelemetry)
            {
                if (!_modelTelemetry.TryGetValue(modelName, out var stats))
                {
                    stats = new ModelTelemetryStats();
                    _modelTelemetry[modelName] = stats;
                }

                stats.TotalRequests++;
                stats.TotalLatencyMs += Math.Max(0, latencyMs);
                if (success) stats.SuccessfulRequests++;
                else stats.FailedRequests++;
                if (isRetry) stats.RetryRequests++;
                _centralTelemetry.TotalModelRequests++;
                _currentSession.ModelRequests++;
                if (success)
                    _centralTelemetry.SuccessfulModelRequests++;
                else
                {
                    _centralTelemetry.FailedModelRequests++;
                    _centralTelemetry.ProviderFailures++;
                    _currentSession.ProviderFailures++;
                }
                if (isRetry) _centralTelemetry.RetryRequests++;
                SaveModelTelemetry();
                SaveCentralTelemetry();
            }
        }

        public IReadOnlyDictionary<string, ToolTelemetryStats> GetToolTelemetrySnapshot()
        {
            lock (_toolTelemetry)
            {
                return new Dictionary<string, ToolTelemetryStats>(_toolTelemetry, StringComparer.OrdinalIgnoreCase);
            }
        }

        public void ClearProjectTelemetry()
        {
            lock (_toolTelemetry)
            {
                _toolTelemetry.Clear();
                _verificationTelemetry = new VerificationTelemetryStats();
                _modelTelemetry.Clear();
                _routerTelemetry = new RouterTelemetryStats();
                _centralTelemetry = new CentralTelemetrySummary();
                _currentSession = new SessionTelemetryRecord { StartedAt = DateTime.UtcNow.ToString("O") };
                SaveTelemetryStats();
                SaveVerificationTelemetry();
                SaveModelTelemetry();
                SaveRouterTelemetry();
                SaveCentralTelemetry();
            }
        }
    }
}
