using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace mdaiAgent;

/// <summary>
/// Handles AI-based plan generation for tasks and goals.
/// Uses IAiProvider (provider abstraction) directly.
/// Not part of main ChatFlowService conversation history.
/// </summary>
public class AiPlanGenerator
{
    private readonly IAiProvider _apiClient;
    private readonly Action<string> _terminalLog;

    public AiPlanGenerator(IAiProvider apiClient, Action<string> terminalLog)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _terminalLog = terminalLog ?? throw new ArgumentNullException(nameof(terminalLog));
    }

    /// <summary>
    /// Generates a detailed AI-based plan for a given objective and context.
    /// Plan is generated in isolation - not added to main ChatFlowService history.
    /// </summary>
    public async Task<string> GeneratePlanAsync(
        string objective,
        string context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objective))
            throw new ArgumentException("Objective cannot be empty.", nameof(objective));

        try
        {
            _terminalLog($"🧠 Plan üretiliyor: {objective}");
            _terminalLog("──────── Plan Akışı ────────");

            // Build plan generation prompt
            var systemPrompt = @"You are a senior software architect. Your job is to produce a concise, actionable implementation plan.

STRICT OUTPUT FORMAT — follow exactly:
1. Output ONLY numbered steps (1. 2. 3. ...). Nothing else.
2. Each step must be a concrete technical action (e.g. 'Create index.html with canvas element', 'Add game loop using requestAnimationFrame').
3. DO NOT output your internal analysis, meta-steps, methodology, or reasoning. No 'Analyze the request', 'Deconstruct the task', 'Identify decision points' — these are forbidden.
4. DO NOT output multiple-choice options or questions as numbered steps.
5. Maximum 10 steps. Each step max 1 sentence.
6. Respond in the same language the user used.

BAD EXAMPLE (never do this):
1. Analyze the user's request
2. Deconstruct the task
3. Identify decision points

GOOD EXAMPLE (do this):
1. Create index.html with a <canvas> element and basic page structure.
2. Implement the Tetris game loop using requestAnimationFrame.
3. Add keyboard event listeners for left/right/rotate/drop controls.";

            var userPrompt = $@"Task: {objective}

Context:
{context}

Produce the numbered implementation steps now:";

            // Create isolated message context (not part of main conversation)
            var messages = new List<ExtendedChatMessage>
            {
                new ExtendedChatMessage { Role = "system", Content = systemPrompt },
                new ExtendedChatMessage { Role = "user",   Content = userPrompt   }
            };

            // Full plan text accumulated from stream
            var planBuilder = new StringBuilder();

            void OnChunk(string? token)
            {
                if (string.IsNullOrEmpty(token)) return;
                planBuilder.Append(token);
            }

            // Streaming call – no tools, pure text generation
            var response = await _apiClient.SendChatWithToolsStreamAsync(
                messages,
                new List<ToolDefinition>(),
                onTokenReceived: OnChunk,
                cancellationToken: cancellationToken);

            // Gelen usage bilgisini TokenTracker'a işle
            if (response?.Usage != null)
            {
                TokenTrackerService.Instance.AddUsage(_apiClient.ModelName, response.Usage);
            }
            else
            {
                // Usage bilgisi stream'den gelmezse, metin uzunluğu üzerinden yaklaşık hesapla
                // 1 token ≈ 4 karakter
                var promptLen = (systemPrompt.Length + userPrompt.Length) / 4;
                var compLen = planBuilder.Length / 4;
                TokenTrackerService.Instance.AddUsage(_apiClient.ModelName, new ChatUsage { PromptTokens = promptLen, CompletionTokens = compLen, TotalTokens = promptLen + compLen });
            }

            var planContent = planBuilder.ToString();
            if (string.IsNullOrWhiteSpace(planContent))
                throw new Exception("AI plan çıktısı boş.");

            _terminalLog("──────── Plan Akışı Sonu ────────");
            _terminalLog($"✅ Plan üretildi ({planContent.Length} karakter)");
            return planContent;
        }
        catch (Exception ex)
        {
            _terminalLog($"❌ Plan üretme hatası: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Generates a recovery plan after an error occurs.
    /// </summary>
    public async Task<string> GenerateRecoveryPlanAsync(
        string objective,
        string errorMessage,
        string context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objective))
            throw new ArgumentException("Objective cannot be empty.", nameof(objective));

        try
        {
            _terminalLog($"🔧 Kurtarma planı üretiliyor...");

            var systemPrompt = @"Sen kriz yönetimi ve hata çözme uzmanısın.
Bir görevde hata oluştu. Senin görevi:
- Hatanın nedenini analiz et
- Minimum adımlarla kurtarma stratejisi oluştur
- Tekrar çalışmayı sağlayacak somut adımları belirt
- Benzer hataları önlemek için ek öneriler sun";

            var userPrompt = $@"Görev: {objective}

Hata: {errorMessage}

Bağlam:
{context}

Bu hatadan kurtulmak için detaylı bir kurtarma planı oluştur.";

            var messages = new List<ExtendedChatMessage>
            {
                new ExtendedChatMessage { Role = "system", Content = systemPrompt },
                new ExtendedChatMessage { Role = "user", Content = userPrompt }
            };

            var response = await _apiClient.SendChatWithToolsAsync(
                messages,
                new List<ToolDefinition>(),
                cancellationToken);

            if (response?.Usage != null)
            {
                TokenTrackerService.Instance.AddUsage(_apiClient.ModelName, response.Usage);
            }
            else
            {
                var promptLen = (systemPrompt.Length + userPrompt.Length) / 4;
                var compLen = (response?.Choices?[0]?.Message?.Content?.Length ?? 0) / 4;
                TokenTrackerService.Instance.AddUsage(_apiClient.ModelName, new ChatUsage { PromptTokens = promptLen, CompletionTokens = compLen, TotalTokens = promptLen + compLen });
            }

            if (response?.Choices == null || response.Choices.Count == 0)
            {
                throw new Exception("Kurtarma planı üretiminde yanıt alınamadı.");
            }

            var recoveryPlan = response.Choices[0]?.Message?.Content ?? "";
            if (string.IsNullOrWhiteSpace(recoveryPlan))
            {
                throw new Exception("Kurtarma planı çıktısı boş.");
            }

            _terminalLog($"✅ Kurtarma planı üretildi");
            return recoveryPlan;
        }
        catch (Exception ex)
        {
            _terminalLog($"❌ Kurtarma planı üretme hatası: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Generates a task dependency graph/structure for a complex task.
    /// </summary>
    public async Task<string> GenerateTaskGraphAsync(
        string objective,
        string context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objective))
            throw new ArgumentException("Objective cannot be empty.", nameof(objective));

        try
        {
            _terminalLog($"🔗 Task graph üretiliyor...");

            var systemPrompt = @"Sen yazılım mimarisi ve proje yönetimi uzmanısın.
Bir görevin yapı ve bağımlılıklarını analiz eden bir task graph oluştur.
Görevler arasındaki ilişkileri, öncelikleri ve parallelleştirilebilir adımları göster.";

            var userPrompt = $@"Görev: {objective}

Bağlam:
{context}

Bu görevin task graph'ını oluştur. Hangi adımlar paralel yapılabilir, hangisinin öncelik olması gerektiğini göster.";

            var messages = new List<ExtendedChatMessage>
            {
                new ExtendedChatMessage { Role = "system", Content = systemPrompt },
                new ExtendedChatMessage { Role = "user", Content = userPrompt }
            };

            var response = await _apiClient.SendChatWithToolsAsync(
                messages,
                new List<ToolDefinition>(),
                cancellationToken);

            if (response?.Usage != null)
            {
                TokenTrackerService.Instance.AddUsage(_apiClient.ModelName, response.Usage);
            }
            else
            {
                var promptLen = (systemPrompt.Length + userPrompt.Length) / 4;
                var compLen = (response?.Choices?[0]?.Message?.Content?.Length ?? 0) / 4;
                TokenTrackerService.Instance.AddUsage(_apiClient.ModelName, new ChatUsage { PromptTokens = promptLen, CompletionTokens = compLen, TotalTokens = promptLen + compLen });
            }

            if (response?.Choices == null || response.Choices.Count == 0)
            {
                throw new Exception("Task graph üretiminde yanıt alınamadı.");
            }

            var taskGraph = response.Choices[0]?.Message?.Content ?? "";
            if (string.IsNullOrWhiteSpace(taskGraph))
            {
                throw new Exception("Task graph çıktısı boş.");
            }

            _terminalLog($"✅ Task graph üretildi");
            return taskGraph;
        }
        catch (Exception ex)
        {
            _terminalLog($"❌ Task graph üretme hatası: {ex.Message}");
            throw;
        }
    }
}
