using System;

using System.Collections.Generic;

using System.IO;

using System.Text;

using System.Linq;

using System.Text.Json;

using System.Text.RegularExpressions;

using System.Threading.Tasks;

using mdaiAgent.Services;

namespace mdaiAgent;

public sealed class ChatFlowMessage

{

    public string Sender { get; init; } = "";

    public string Content { get; init; } = "";

    public bool ShouldRefreshFileTree { get; init; } = false;

}

public sealed class ChatFlowResult

{

    public bool Success { get; set; }

    public string ErrorMessage { get; set; } = "";

    public List<ChatFlowMessage> Messages { get; set; } = new();

    public List<string> UpdatedFilePaths { get; set; } = new();

    public VerificationResult? LastVerificationResult { get; set; }

}

public class ChatFlowService : IChatFlowService

{

    private readonly ChatSessionService _sessionService;

    private readonly Action<string> _terminalLog;

    private readonly Action<string, NotificationSeverity> _notify;

    private readonly Action<string> _updateOperationStep;

    private IAiProvider? _apiClient;

    private ToolExecutor? _toolExecutor;

    private AgentCoreRuntime? _safeRuntime;

    private AgentVerificationLoopService? _verificationService;

    private RagService? _ragService;

    private System.Timers.Timer? _memoryCleanupTimer;

    private const int MaxHistoryMessagesPerSession = 200;   // Oturumda saklanacak maksimum mesaj

    private const int MaxHistoryMessagesToSend = 40;         // Fallback: mesaj sayısı sınırı

    private const string ResponseLanguageInstruction = "<response_language>\nRespond in the same language as the user's latest message unless the user explicitly requests another language. The application's interface language and the language of these system instructions do not determine the response language. All tool call arguments (including questions, options, and descriptions in AskUserOptions) MUST ALSO be generated in the user's language. Preserve code, commands, file paths, and technical identifiers exactly when appropriate.\n</response_language>";

    // [A] System Prompt Cache — her mesajda constitution.md diskten okunmaz

    private string? _cachedSystemPrompt;

    private string? _cachedConstitutionFolder;  // Hangi klasör için cache'lendi

    private DateTime _constitutionCacheTime = DateTime.MinValue;

    private static readonly TimeSpan ConstitutionCacheDuration = TimeSpan.FromSeconds(30);

    // [B] Token Bazlı History Budget

    // ~4 karakter = 1 token kaba tahmini (GPT-style)

    private const int LocalModelHistoryTokenBudget  = 6_000;   // ~6K token → ~24K karakter

    private const int CloudModelHistoryTokenBudget  = 16_000;  // ~16K token → ~64K karakter

    public List<ChatSession> ChatSessions { get; private set; } = new();

    public ChatSession? ActiveSession { get; private set; }

    public event Action? SessionsUpdated;

    public event Action? ActiveSessionChanged;

    public event Action<string>? FileModifiedByAi;

    public ChatFlowService(

        ChatSessionService sessionService,

        Action<string> terminalLog,

        Action<string, NotificationSeverity> notify,

        Action<string> updateOperationStep)

    {

        _sessionService = sessionService;

        _terminalLog = terminalLog;

        _notify = notify;

        _updateOperationStep = updateOperationStep;

    }

    public void UpdateClients(IAiProvider? apiClient, ToolExecutor? toolExecutor)

    {

        _apiClient = apiClient;

        _toolExecutor = toolExecutor;

        // Keep the active production path untouched, but install a safe adapter
        // so the runtime abstraction is available for the next cutover without
        // changing current ChatFlowService behavior.
        if (_toolExecutor != null)

        {

            _verificationService = new AgentVerificationLoopService(_toolExecutor, this, _terminalLog);

            _safeRuntime = AgentCoreRuntime.CreateSafeRuntime(
                _toolExecutor.ProjectFolder ?? string.Empty,
                _terminalLog,
                _notify,
                _updateOperationStep,
                _toolExecutor,
                verificationService: _verificationService);

        }

    }

    public AgentCoreRuntime? SafeRuntime => _safeRuntime;

    public void UpdateRagService(RagService? ragService)

    {

        _ragService = ragService;

    }

    public void Initialize()

    {

        ChatSessions = _sessionService.LoadSessions();

        if (ChatSessions.Count == 0)

        {

            ActiveSession = CreateNewSessionInternal(Localization.Get("Yeni Sohbet", "New Chat"));

            SaveSessions();

        }

        else

        {

            ActiveSession = ChatSessions[0];

        }

        // Bellek temizleme zamanlayıcısını başlat (30 dakikada bir)

        StartMemoryCleanupTimer();

        SessionsUpdated?.Invoke();

        ActiveSessionChanged?.Invoke();

    }

    private void StartMemoryCleanupTimer()

    {

        _memoryCleanupTimer = new System.Timers.Timer(TimeSpan.FromMinutes(30).TotalMilliseconds);

        _memoryCleanupTimer.Elapsed += OnMemoryCleanupTimerElapsed;

        _memoryCleanupTimer.AutoReset = true;

        _memoryCleanupTimer.Start();

    }

    private void OnMemoryCleanupTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)

    {

        try

        {

            CleanupOldChatHistory();

        }

        catch (Exception ex)

        {

            _terminalLog?.Invoke($"[Bellek Temizliği Hatası] {ex.Message}");

        }

    }

    private void CleanupOldChatHistory()

    {

        bool anySessionChanged = false;

        foreach (var session in ChatSessions)

        {

            if (session.History.Count > MaxHistoryMessagesPerSession)

            {

                // Sadece en eski mesajları sil, son 5000 mesajı koru

                int messagesToRemove = session.History.Count - MaxHistoryMessagesPerSession;

                session.History.RemoveRange(0, messagesToRemove);

                anySessionChanged = true;

                _terminalLog?.Invoke($"[Bellek Temizliği] {session.Name} oturumundan {messagesToRemove} eski mesaj temizlendi");

            }

        }

        if (anySessionChanged)

        {

            SaveSessions();

        }

        // GC'yi tetikle (isteğe bağlı, ancak bellek kullanımını düşürmeye yardımcı olabilir)

        GC.Collect();

        GC.WaitForPendingFinalizers();

    }

    public void ReloadSessions()

    {

        var previousActiveId = ActiveSession?.Id;

        ChatSessions = _sessionService.LoadSessions();

        if (ChatSessions.Count == 0)

        {

            ActiveSession = CreateNewSessionInternal(Localization.Get("Yeni Sohbet", "New Chat"));

            SaveSessions();

        }

        else if (!string.IsNullOrEmpty(previousActiveId))

        {

            ActiveSession = ChatSessions.FirstOrDefault(s => s.Id == previousActiveId) ?? ChatSessions[0];

        }

        else

        {

            ActiveSession = ChatSessions[0];

        }

        SessionsUpdated?.Invoke();

        ActiveSessionChanged?.Invoke();

    }

    public ChatSession CreateNewSession(string name)

    {

        var session = CreateNewSessionInternal(name);

        SaveSessions();

        SessionsUpdated?.Invoke();

        ActiveSessionChanged?.Invoke();

        return session;

    }

    public bool DeleteActiveSession()

    {

        if (ActiveSession == null || ChatSessions.Count <= 1)

            return false;

        var index = ChatSessions.IndexOf(ActiveSession);

        var removed = ChatSessions.Remove(ActiveSession);

        if (!removed)

            return false;

        var nextIndex = Math.Max(0, index - 1);

        ActiveSession = ChatSessions[nextIndex];

        SaveSessions();

        SessionsUpdated?.Invoke();

        ActiveSessionChanged?.Invoke();

        return true;

    }

    public void ClearActiveSessionHistory()

    {

        if (ActiveSession == null)

            return;

        ActiveSession.History.Clear();

        ActiveSession.Name = Localization.Get("Yeni Sohbet", "New Chat");

        SaveSessions();

        ActiveSessionChanged?.Invoke();

    }

    public void SetActiveSession(ChatSession session)

    {

        if (session == null)

            return;

        ActiveSession = session;

        ActiveSessionChanged?.Invoke();

    }

    public Task<ChatFlowResult> SendMessageAsync(

        string message,

        string? currentFilePath,

        string? currentFileContent,

        string? selectedFolder,

        string systemPrompt)

    {

        return SendMessageAsync(message, currentFilePath, currentFileContent, selectedFolder, systemPrompt, false, false, null, CancellationToken.None, null, null, true, null);

    }

    public Task<ChatFlowResult> SendMessageAsync(

        string message,

        string? currentFilePath,

        string? currentFileContent,

        string? selectedFolder,

        string systemPrompt,

        bool qaAgentEnabled,

        bool uiAgentEnabled,

        List<Attachment>? attachments = null,

        CancellationToken cancellationToken = default,

        Action<string>? onTokenReceived = null,

        Action<ChatFlowMessage>? onMessageAdded = null,

        bool appendUserMessageToHistory = true,

        ChatSession? targetSession = null,

        ChatContextMode contextMode = ChatContextMode.Auto)

    {

        return SendMessageAsync(message, currentFilePath, currentFileContent, selectedFolder, systemPrompt, qaAgentEnabled, uiAgentEnabled, attachments, cancellationToken, onTokenReceived, onMessageAdded, false, appendUserMessageToHistory, targetSession, contextMode);

    }

    public Task<ChatFlowResult> SendMessageAsync(

        string message,

        string? currentFilePath,

        string? currentFileContent,

        string? selectedFolder,

        string systemPrompt,

        bool qaAgentEnabled,

        bool uiAgentEnabled,

        List<Attachment>? attachments,

        CancellationToken cancellationToken,

        Action<string>? onTokenReceived,

        Action<ChatFlowMessage>? onMessageAdded,

        bool appendUserMessageToHistory,

        ChatSession? targetSession)
    {
        return SendMessageAsync(message, currentFilePath, currentFileContent, selectedFolder, systemPrompt, qaAgentEnabled, uiAgentEnabled, attachments, cancellationToken, onTokenReceived, onMessageAdded, appendUserMessageToHistory, targetSession, ChatContextMode.Auto);
    }

    public Task<ChatFlowResult> SendMessageAsync(

        string message,

        string? currentFilePath,

        string? currentFileContent,

        string? selectedFolder,

        string systemPrompt,

        bool qaAgentEnabled,

        bool uiAgentEnabled,

        List<Attachment>? attachments,

        CancellationToken cancellationToken,

        Action<string>? onTokenReceived,

        Action<ChatFlowMessage>? onMessageAdded,

        bool includeSystemPrompt,

        bool appendUserMessageToHistory = true,

        ChatSession? targetSession = null,

        ChatContextMode contextMode = ChatContextMode.Auto)

    {

        return SendMessageInternalAsync(message, currentFilePath, currentFileContent, selectedFolder, systemPrompt, qaAgentEnabled, uiAgentEnabled, attachments, cancellationToken, onTokenReceived, onMessageAdded, includeSystemPrompt, appendUserMessageToHistory, targetSession, contextMode);

    }

    public async Task<ChatFlowResult> SendMessageInternalAsync(

        string message,

        string? currentFilePath,

        string? currentFileContent,

        string? selectedFolder,

        string systemPrompt,

        bool qaAgentEnabled,

        bool uiAgentEnabled,

        List<Attachment>? attachments = null,

        CancellationToken cancellationToken = default,

        Action<string>? onTokenReceived = null,

        Action<ChatFlowMessage>? onMessageAdded = null,

        bool includeSystemPrompt = true,

        bool appendUserMessageToHistory = true,

        ChatSession? targetSession = null,

        ChatContextMode contextMode = ChatContextMode.Auto,

        bool skipVerification = false)

    {

        var session = targetSession ?? ActiveSession;

        if (string.IsNullOrWhiteSpace(message))

        {

            return new ChatFlowResult { Success = false, ErrorMessage = "Mesaj boş." };

        }

        // ⚡ ÇALIŞMA MODU BYPASS — Özel modlarda LLM'yi atla veya yönlendir
        var activeMode = SettingsWindow.GetSettings().ActiveWorkspaceMode;
        if (activeMode == AgentWorkspaceMode.ImageStudio)
        {
            return await HandleImageStudioRequestAsync(message, session, onTokenReceived, onMessageAdded, cancellationToken);
        }
        if (activeMode == AgentWorkspaceMode.BlenderCopilot)
        {
            return await HandleBlenderRequestAsync(message, session, onTokenReceived, onMessageAdded, cancellationToken);
        }
        if (activeMode == AgentWorkspaceMode.UnityCopilot)
        {
            return await HandleUnityRequestAsync(message, session, onTokenReceived, onMessageAdded, cancellationToken);
        }

        // 🔍 Otomatik Context Discovery
        // Sadece bulut modeller için çalıştır. Yerel modellerde (Ollama/LM Studio)
        // bu tarama prompt boyutunu inanılmaz şişirerek TTFT'nin 80+ sn olmasına neden olur.
        string enhancedMessage = message;
        var discoveredSnippets = new Dictionary<string, List<CodeSnippet>>(StringComparer.OrdinalIgnoreCase);

        bool isLocalModel = _apiClient is OpenAiCompatibleClient oacCheck && oacCheck.ResolveConnectionSettings().UseLocalModel;

        if (!isLocalModel || contextMode == ChatContextMode.Rag)

        {

            if (!isLocalModel && (contextMode == ChatContextMode.Auto || contextMode == ChatContextMode.Project))
            {
                try
                {
                    var projectRoot = _toolExecutor?.ProjectFolder ?? "";

                    if (!string.IsNullOrEmpty(projectRoot))
                    {
                        var discoveryService = new ProjectContextDiscoveryService(projectRoot, _terminalLog);
                        var discoveredContext = await discoveryService.DiscoverContextFromMessageAsync(message);
                        discoveredSnippets = discoveredContext.CodeSnippets;

                        if (!string.IsNullOrEmpty(discoveredContext.ContextMarkdown))
                        {
                            enhancedMessage = $"{message}\n\n---\n\n{discoveredContext.ContextMarkdown}";
                            _terminalLog?.Invoke(
                                LocalizationManager.Instance
                                    .GetString("ContextDiscoveryKeywordsCountKeywordRelevantFilesCountDosyaContextCodeSnippetsSumXXValueCountSnippetKesfedildi")
                                    .Replace("{keywords.Count}", discoveredContext.ExtractedKeywords.Count.ToString())
                                    .Replace("{relevantFiles.Count}", discoveredContext.RelevantFiles.Count.ToString())
                                    .Replace("{context.CodeSnippets.Sum(x => x.Value.Count)}", discoveredContext.CodeSnippets.Sum(x => x.Value.Count).ToString()));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _terminalLog?.Invoke($"[Context Discovery Deactivated] {ex.Message}");
                }
            }

            // 🧠 RAG Context Injection (from vector database)

            try

            {

                if (_ragService != null && (contextMode == ChatContextMode.Auto || contextMode == ChatContextMode.Rag))

                {

                    var ragResults = (await _ragService.SearchAsync(message, topK: contextMode == ChatContextMode.Rag ? 5 : 3))
                        .Where(result => !OverlapsDiscoveredSnippet(result, discoveredSnippets))
                        .GroupBy(result => $"{Path.GetFullPath(result.FilePath)}:{result.StartLine}:{result.EndLine}", StringComparer.OrdinalIgnoreCase)
                        .Select(group => group.OrderByDescending(result => result.Similarity).First())
                        .ToList();

                    if (ragResults.Count > 0)

                    {

                        var ragContextBuilder = new StringBuilder();

                        ragContextBuilder.AppendLine("\n---\n## Code Context (from RAG):");

                        foreach (var ragChunk in ragResults)

                        {

                            ragContextBuilder.AppendLine($"\n### {ragChunk.ChunkType}: {ragChunk.ChunkName}");

                            ragContextBuilder.AppendLine($"📄 {ragChunk.FilePath} (lines {ragChunk.StartLine}-{ragChunk.EndLine})");

                            ragContextBuilder.AppendLine("```csharp");

                            var codePreview = ragChunk.CodeSnippet.Length > 300 

                                ? ragChunk.CodeSnippet.Substring(0, 300) + "..." 

                                : ragChunk.CodeSnippet;

                            ragContextBuilder.AppendLine(codePreview);

                            ragContextBuilder.AppendLine("```");

                            ragContextBuilder.AppendLine($"🎯 Relevance: {(ragChunk.Similarity * 100):F1}%");

                        }

                        enhancedMessage = enhancedMessage + ragContextBuilder.ToString();

                        _terminalLog?.Invoke($"🧠 RAG: {ragResults.Count} relevant code chunks added to context");

                    }

                }

            }

            catch (Exception ex)

            {

                _terminalLog?.Invoke($"[RAG Context Injection Failed] {ex.Message}");

            }

        }

        else

        {

            // _terminalLog?.Invoke("⚡ [Local Model] Context Discovery atlandı (token tasarrufu).");

        }

        string modifiedMessage = enhancedMessage;

        if (message.TrimStart().StartsWith("/"))

        {

            var parts = message.TrimStart().Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);

            var cmd = parts[0].ToLowerInvariant();

            var args = parts.Length > 1 ? parts[1] : "";

            switch (cmd)

            {

                case "/constitution":

                    modifiedMessage = $"[ÖZEL TALİMAT: PROJE ANAYASASI OLUŞTURMA]\nKullanıcı senden bu proje için bir '.mdai/constitution.md' (Proje Anayasası) dosyası oluşturmanı istiyor. Bu dosya, projede kullanılacak teknolojileri, kodlama standartlarını ve mimari kuralları içermelidir. KOD YAZMA, sadece bu markdown dosyasını (CreateOrUpdateFile aracı ile) oluştur veya güncelle.\n\nKullanıcı Talebi: {args}";

                    break;

                case "/spec":

                    modifiedMessage = $"[ÖZEL TALİMAT: YAZILIM SPESİFİKASYONU OLUŞTURMA]\nKullanıcı senden bir yazılım spesifikasyonu (örn: '.mdai/spec.md') oluşturmanı istiyor. Özellikleri, kullanıcı hikayelerini ve gereksinimleri detaylandır. HİÇBİR ŞEKİLDE KOD YAZMA. Sadece spesifikasyonu markdown olarak oluştur.\n\nKullanıcı Talebi: {args}";

                    break;

                case "/plan":

                    modifiedMessage = $"[ÖZEL TALİMAT: MİMARİ PLAN OLUŞTURMA]\nKullanıcı senden teknik bir mimari plan (örn: '.mdai/implementation_plan.md') oluşturmanı istiyor. Hangi teknolojilerin kullanılacağını, hangi dosyaların ekleneceğini/değiştirileceğini detaylandır. HİÇBİR ŞEKİLDE KOD YAZMA. Sadece planı markdown olarak oluştur.\n\nKullanıcı Talebi: {args}";

                    break;

                case "/tasks":

                    modifiedMessage = $"[ÖZEL TALİMAT: GÖREV LİSTESİ OLUŞTURMA]\nKullanıcı senden mevcut planı adım adım uygulanabilir bir görev listesine (örn: '.mdai/task.md') dönüştürmeni istiyor. Check-box'lar içeren bir liste yap. HİÇBİR ŞEKİLDE KOD YAZMA. Sadece görev listesini oluştur.\n\nKullanıcı Talebi: {args}";

                    break;

                case "/implement":

                    modifiedMessage = $"[ÖZEL TALİMAT: KODLAMA VE UYGULAMA]\nKullanıcı senden daha önce oluşturulan plan ve görev listesine (task.md) sadık kalarak kodlamaya başlamanı istiyor. Planı oku ve ilgili dosyaları oluştur, düzenle ve görevleri tamamla.\n\nKullanıcı Talebi: {args}";

                    break;

            }

        }

        if (session == null)

        {

            session = CreateNewSessionInternal(Localization.Get("Yeni Sohbet", "New Chat"));

            if (targetSession == null) ActiveSession = session;

        }

        if (session.Name == Localization.Get("Yeni Sohbet", "New Chat") || string.IsNullOrEmpty(session.Name))

        {

            var cleanName = message.Length > 25 ? message.Substring(0, 22) + "..." : message;

            session.Name = cleanName;

        }

        if (includeSystemPrompt && session.History.Count == 0)

        {

            session.History.Add(new ExtendedChatMessage

            {

                Role = "system",

                Content = !string.IsNullOrEmpty(systemPrompt) ? systemPrompt : ToolRegistry.GetSystemPrompt()

            });

        }

        if (appendUserMessageToHistory)

        {

            var userMessage = new ExtendedChatMessage { Role = "user", Content = modifiedMessage, Attachments = attachments };

            session.History.Add(userMessage);

            SaveSessions();

        }

        EventBus.Publish(new TimelineEvent

        {

            Type = TimelineEventType.Info,

            Message = message,

            Details = "Kullanıcı bir görev gönderdi.",

            IsExpanded = false

        });

        if (_apiClient == null || _toolExecutor == null)

        {

            return new ChatFlowResult

            {

                Success = false,

                ErrorMessage = "AI veya araç hizmeti yapılandırılmadı.",

                Messages = new List<ChatFlowMessage>

                {

                    new ChatFlowMessage { Sender = "AI Asistan", Content = LocalizationManager.Instance.GetString("LutfenAyarlardanAPIAnahtariniziEkleyin") }

                }

            };
        }

        var result = new ChatFlowResult

        {

            Success = true,

            Messages = new List<ChatFlowMessage>(),

            UpdatedFilePaths = new List<string>()

        };

        try

        {

            // ─── AKILLI GÖREV SINIFLANDIRICI ───────────────────────────────────────

            // Kural 1: "/plan" komutu → her zaman planla (kullanıcı explicit istedi)

            // Kural 2: Çok kısa cümle (≤30 char) → basit görev, plan yok

            // Kural 3: YEREL MODEL + Router kapalı → heuristik ile sınıflandır

            // Kural 4: BULUT MODEL + Router açık → Router kararı kullanılır (aşağıda)

            var settings = SettingsWindow.GetSettings();

            bool needsPlan = false;

            // Kural: Manuel araç seçimi varsa Router kesinlikle devre dışıdır!

            bool routerIsActive = !settings.IsManualToolManagement && (settings.RouterUseLocalModel || settings.RouterEnabled || settings.RouterUseMainModel);
            bool isContinuationMessage = IsContinuationMessage(message);
            bool skipRouterForCasualMessage = routerIsActive && !isContinuationMessage && IsCasualMessage(message);

            if (message.StartsWith("/plan", StringComparison.OrdinalIgnoreCase))

            {

                needsPlan = true;

                message = message.Substring("/plan".Length).Trim();

            }

            else if (message.Length <= 30)

            {

                needsPlan = false;

            }

            else if (isLocalModel || !routerIsActive)

            {

                // Router kapalı veya yerel model: sıfır maliyetli heuristik kullan.

                needsPlan = IsComplexTaskHeuristic(message);

            }

            // Router açıksa: needsPlan Router'dan gelecek (aşağıda override edilir)

            // ─────────────────────────────────────────────────────────────────────

            List<ToolDefinition> allActiveTools;

            if (routerIsActive && !skipRouterForCasualMessage)

            {

                // Router aktifse: Manuel seçim/Araçlar sekmesi tamamen YOK SAYILIR (By-pass).

                // Sadece Router'a özel Kara Liste'deki araçlar çıkarılır.

                var blacklistedTools = settings.RouterBlacklistedTools ?? new List<string>();

                allActiveTools = ToolRegistry.GetTools()

                    .Where(t => t.Function?.Name != null && !blacklistedTools.Contains(t.Function.Name))

                    .ToList();

            }

            else

            {

                // Router kapalıysa: Kullanıcının manuel araç yönetimi (Araçlar sekmesi) geçerlidir.

                if (settings.IsManualToolManagement)

                {

                    var disabledTools = settings.DisabledTools ?? new List<string>();

                    allActiveTools = ToolRegistry.GetTools()

                        .Where(t => t.Function?.Name != null && !disabledTools.Contains(t.Function.Name))

                        .ToList();

                }

                else

                {

                    allActiveTools = ToolRegistry.GetTools()

                        .Where(t => t.Function?.Name != null)

                        .ToList();

                }

            }

            var messagesToSend = BuildMessages(message, currentFilePath, currentFileContent, selectedFolder, systemPrompt, allActiveTools, isLocalModel);

            if (messagesToSend.Count > 0)

            {

            }

            int iteration = 0;

            int maxIterations = 10;

            const int maxSameToolRetry = 3;
            var toolFailureStreak = new Dictionary<(string ToolName, int ArgsHash), int>();
            var toolArgsHashCache = new Dictionary<(string, string), int>();
            bool loopGuardTriggered = false;
            string? loopGuardMessage = null;
            VerificationResult? lastVerificationResult = null;

            ExtendedChatMessage? assistantMessage = null;

            bool fallbackQuestionsHandled = false;

            var activeTools = allActiveTools;

            var routerSelectionApplied = false;

            var planCreatedForCurrentTask = false;
            var planContinuationAttempts = 0;

            if (routerIsActive && !skipRouterForCasualMessage)

            {

                // Router için kullanılacak bağlantı bilgilerini belirle

                string routerApiKey;

                string routerModel;

                string routerBaseUrl;

                if (settings.RouterUseLocalModel)
                {
                    routerApiKey  = "local_ollama";
                    routerModel   = string.IsNullOrWhiteSpace(settings.RouterModel)
                        ? RouterModelInfo.OllamaModelName
                        : settings.RouterModel.Trim();
                    routerBaseUrl = string.IsNullOrWhiteSpace(settings.RouterBaseUrl)
                        ? RouterModelInfo.DefaultBaseUrl
                        : settings.RouterBaseUrl.TrimEnd('/');
                }
                else if (settings.RouterUseMainModel)
                {

                    var mc = settings.ActiveProvider;

                    if (mc == ProviderType.Google)

                    {

                        routerApiKey  = settings.GoogleApiKey ?? "";

                        routerModel   = settings.GoogleModel;

                        routerBaseUrl = settings.GoogleBaseUrl.TrimEnd('/').Replace("/openai", "", StringComparison.OrdinalIgnoreCase) + "/openai";

                    }

                    else if (mc == ProviderType.Anthropic)

                    {

                        routerApiKey  = settings.AnthropicApiKey ?? "";

                        routerModel   = settings.AnthropicModel;

                        routerBaseUrl = "https://api.anthropic.com/v1";

                    }

                    else

                    {

                        routerApiKey  = settings.ApiKey ?? "";

                        routerModel   = settings.Model;

                        routerBaseUrl = settings.BaseUrl;

                    }

                }

                else

                {

                    routerApiKey  = settings.RouterApiKey ?? "";

                    routerModel   = settings.RouterModel;

                    routerBaseUrl = !string.IsNullOrWhiteSpace(settings.RouterBaseUrl)

                        ? settings.RouterBaseUrl

                        : RouterModelInfo.DefaultBaseUrl;

                }

                if (!string.IsNullOrWhiteSpace(routerApiKey))
                {
                    var routerAnalyzingMessage = LocalizationManager.Instance.GetString("RouterGorevAnalizEdiliyorAracSecimi");
                    _terminalLog?.Invoke(routerAnalyzingMessage);
                    _updateOperationStep?.Invoke(routerAnalyzingMessage);

                    var router = new AiRouter(routerApiKey, routerModel, routerBaseUrl);

                    var routerContext = new RouterContext { AvailableTools = allActiveTools, SystemPrompt = systemPrompt };

                    var routerStopwatch = System.Diagnostics.Stopwatch.StartNew();
                    var decision = await router.RouteAsync(message, routerContext, cancellationToken);
                    routerStopwatch.Stop();

                    var isTrustedYengiRouter = settings.RouterUseLocalModel
                        && string.Equals(
                            settings.RouterModel?.Trim(),
                            RouterModelInfo.OllamaModelName,
                            StringComparison.OrdinalIgnoreCase);
                    var routerConfidenceThreshold = isTrustedYengiRouter
                        ? 0.0
                        : settings.GetNormalizedRouterConfidenceThreshold();
                    var routerTelemetryStatus = "fallback-error";
                    var routerTelemetryReason = decision?.ErrorMessage ?? "Router geçerli bir karar döndürmedi.";
                    if (decision != null && string.IsNullOrEmpty(decision.ErrorMessage) && decision.Confidence >= routerConfidenceThreshold)
                    {
                        // Router'ın needsPlan kararı heuristik kararını override eder
                        needsPlan = decision.NeedsPlanning;

                        if (decision.Tools.Count == 0)
                        {
                            activeTools = new List<ToolDefinition>();
                            routerTelemetryStatus = "empty-selection";
                            routerTelemetryReason = "Router araç gerekmeyen bir istek belirledi.";
                        }
                        else
                        {
                            var selectedTools = ToolCapabilityScoring.OrderByScore(
                                allActiveTools.Where(t => decision.Tools.Contains(t.Function!.Name!)))
                                .ToList();

                            if (!selectedTools.Any())
                            {
                                activeTools = GetRouterFallbackTools(allActiveTools);
                                routerTelemetryStatus = "fallback-invalid-selection";
                                routerTelemetryReason = "Router yalnızca aktif katalogda bulunmayan araçlar seçti.";
                            }
                            else
                            {
                                activeTools = selectedTools;

                                // 🛠️ KOŞULLU ÇEKİRDEK ARAÇ KORUMASI (Option 1):
                                // Sadece kodlama/dosya yazma/planlama ihtiyacı olduğunda terminal aracını ekle.
                                // Düz okuma/arama sorgularında terminal aracı eklenmeyerek TOKEN ve LATENCY tasarrufu sağlanır.
                                bool isModifyingOrPlanning = needsPlan || selectedTools.Any(t => 
                                    t.Function?.Name == "CreateOrUpdateFile" || 
                                    t.Function?.Name == "ReplaceFileContent" || 
                                    t.Function?.Name == "DeleteFile" || 
                                    t.Function?.Name == "CreateFolder");

                                if (isModifyingOrPlanning)
                                {
                                    var terminalTool = allActiveTools.FirstOrDefault(t => string.Equals(t.Function?.Name, "ExecuteTerminalCommand", StringComparison.OrdinalIgnoreCase));
                                    if (terminalTool != null && !activeTools.Any(t => string.Equals(t.Function?.Name, "ExecuteTerminalCommand", StringComparison.OrdinalIgnoreCase)))
                                    {
                                        activeTools.Add(terminalTool);
                                    }
                                }

                                routerTelemetryStatus = "selected";
                                routerTelemetryReason = $"{activeTools.Count} araç seçildi.";
                            }
                        }

                        routerSelectionApplied = true;

                        // Open Source Transparency: Yengi Router (fine-tuned) doesn't output confidence score.
                        // Instead of faking a percentage, we transparently state that Yengi Router is handling the confidence.
                        string displayConfidenceStr = decision.Confidence.ToString("P0");
                        if (isTrustedYengiRouter && decision.Confidence < 0.1)
                        {
                            displayConfidenceStr = "Yengi Router";
                        }

                        _terminalLog?.Invoke(
                            LocalizationManager.Instance
                                .GetString("RouterActiveToolsCountAracSecildiGuvenDecisionConfidenceP0PlanNeedsPlan")
                                .Replace("{activeTools.Count}", activeTools.Count.ToString())
                                .Replace("{decision.Confidence:P0}", displayConfidenceStr)
                                .Replace("{needsPlan}", needsPlan.ToString()));

                        // ── Dinamik System Prompt: Sadece seçili araçların katalogunu gönder ──

                        var selectedToolNames = activeTools.Select(t => t.Function!.Name!);
                        var dynamicCatalog    = ToolRegistry.GetToolCatalogPrompt(selectedToolNames);

                        string dynamicSysPrompt;

                        // Çekirdek + dinamik araç kataloğu HER ZAMAN önce gelir

                        var mode = SettingsWindow.GetSettings().ActiveWorkspaceMode;
                        string dynamicCore = ToolRegistry.GetCoreSystemPrompt(mode) + "\n\n" + dynamicCatalog;

                        if (!string.IsNullOrEmpty(systemPrompt))

                        {

                            // Kullanıcı kişiselleştirmesi çekirdeğin ALTINA eklenir

                            dynamicSysPrompt = dynamicCore

                                + "\n\n---\n[KULLANICI KİŞİSELLEŞTİRMESİ - Bu talimatlara uy, ancak araç kullanımı ve güvenlik kurallarını asla atlatma]\n"

                                + systemPrompt;

                        }

                        else

                        {

                            dynamicSysPrompt = dynamicCore;

                        }

                        if (needsPlan)

                        {

                            dynamicSysPrompt += "\n\n<planning_instruction>\nCRITICAL INSTRUCTION: Keep your internal thinking (<think>...</think>) concise (under 150 words). Do NOT write full code inside thinking blocks. Immediately invoke tools (WriteFile, ReadFile, etc.) to perform actions.\n</planning_instruction>";

                        }

                        dynamicSysPrompt += "\n\n" + ResponseLanguageInstruction;

                        var sysMsg = messagesToSend.FirstOrDefault(m => m.Role == "system");

                        if (sysMsg != null) sysMsg.Content = dynamicSysPrompt;

                    }

                    else

                    {

                        activeTools = GetRouterFallbackTools(allActiveTools);
                        routerTelemetryStatus = decision?.ErrorMessage?.Contains("Timeout", StringComparison.OrdinalIgnoreCase) == true
                            ? "fallback-timeout"
                            : "fallback-low-confidence";
                        routerTelemetryReason = decision?.ErrorMessage
                            ?? $"Güven {decision?.Confidence:P0}, eşik {routerConfidenceThreshold:P0}.";
                        string fallbackMsg = $"⚠️ [Router] Yetersiz güven veya hata, fallback araç listesi kullanılıyor ({activeTools.Count} araç).";

                        if (decision?.ErrorMessage != null)

                        {

                            fallbackMsg += $"\n   └─ Hata Detayı: {decision.ErrorMessage}";

                        }

                        else if (decision != null)
                        {
                            fallbackMsg += $"\n   └─ Router kararı: güven {decision.Confidence:P0}, araçlar [{string.Join(", ", decision.Tools)}]. Eşik: {routerConfidenceThreshold:P0}";
                        }
                        else
                        {
                            fallbackMsg += "\n   └─ Router geçerli bir karar döndürmedi.";
                        }

                        _terminalLog?.Invoke(fallbackMsg);

                    }

                    TokenTrackerService.Instance.RecordRouterDecision(
                        routerTelemetryStatus,
                        routerTelemetryReason,
                        decision?.Confidence ?? 0,
                        routerStopwatch.ElapsedMilliseconds);

                }

                if (!routerSelectionApplied)
                {
                    activeTools = GetRouterFallbackTools(allActiveTools);
                    UpdateSystemPromptForSelectedTools(messagesToSend, activeTools, systemPrompt, needsPlan);
                }

            }

            if (skipRouterForCasualMessage)
            {
                activeTools = new List<ToolDefinition>();
                UpdateSystemPromptForSelectedTools(messagesToSend, activeTools, systemPrompt, needsPlan);
                _terminalLog?.Invoke(LocalizationManager.Instance.GetString("RouterSimpleChatSkipped"));
            }

            // needsPlan → CreatePlan: Yalnızca Router KAPALI veya kullanıcı /plan komutu verdiyse

            // çalışır. Router açıkken planning instruction system prompt'a eklendi (yukarıda).

            if (needsPlan && !routerIsActive && _toolExecutor != null && !string.IsNullOrWhiteSpace(selectedFolder))

            {

                try

                {

                    var planArgs = new { task = message.Trim(), projectPath = selectedFolder };

                    var planResult = await _toolExecutor.ExecuteAsync("CreatePlan", JsonSerializer.Serialize(planArgs), cancellationToken);

                    if (planResult.Success)

                    {

                        planCreatedForCurrentTask = true;

                        _terminalLog?.Invoke(LocalizationManager.Instance.GetString("PlanCreatedAndSaved"));

                        string planText = planResult.Output;

                        try

                        {

                            var planJson = JsonDocument.Parse(planResult.Output).RootElement;

                            if (planJson.TryGetProperty("plan", out var planProp))

                                planText = planProp.GetString() ?? planResult.Output;

                        }

                        catch { }

                        result.Messages.Add(new ChatFlowMessage

                        {

                            Sender = "Plan Generator",

                            Content = LocalizationManager.Instance.GetString("PlanOlusturulduPlanText").Replace("{planText}", planText)

                        });

                        var planMessage = new ExtendedChatMessage

                        {

                            Role = "system",

                            Content = LocalizationManager.Instance.GetString("SISTEMBILGISIBuGorevIcinAsagidakiPlanOlusturulduLutfenAdimlaraSadikKalarakIlerlePlanText").Replace("{planText}", planText)

                        };

                        session?.History.Add(planMessage);

                        messagesToSend.Add(planMessage);

                        SaveSessions();

                    }

                }

                catch (Exception ex)

                {

                    _terminalLog?.Invoke($"⚠️ CreatePlan hatası (devam ediliyor): {ex.Message}");

                }

            }

            while (iteration < maxIterations)

            {

                cancellationToken.ThrowIfCancellationRequested();

                _updateOperationStep?.Invoke($"🧠 AI yanıtı işliyor (tur {iteration + 1})...");

                ExtendedChatResponse? response = null;

                StringBuilder? tokenBuffer = null;

                try

                {

                    if (onTokenReceived != null)

                    {

                        tokenBuffer = new StringBuilder();

                        void CombinedToken(string? token)

                        {

                            var safeToken = token ?? string.Empty;

                            try { tokenBuffer.Append(safeToken); } catch { }

                            var tokenReceiver = onTokenReceived;

                            if (tokenReceiver != null)

                            {

                                try { tokenReceiver(safeToken); } catch { }

                            }

                        }

                        response = await _apiClient.SendChatWithToolsStreamAsync(messagesToSend, activeTools, CombinedToken, cancellationToken);

                    }

                    else

                    {

                        response = await _apiClient.SendChatWithToolsAsync(messagesToSend, activeTools, cancellationToken);

                    }

                }

                catch (OperationCanceledException)

                {

                    // Kullanıcı iptal ettiyse ve tokenBuffer'da yarım kalmış bir cevap varsa, bunu geçmişe ekle ki unutmasın

                    if (tokenBuffer != null && tokenBuffer.Length > 0)

                    {

                        var partialMessage = new ExtendedChatMessage

                        {

                            Role = "assistant",

                            Content = tokenBuffer.ToString()

                        };

                        session?.History.Add(partialMessage);

                        SaveSessions();

                        _terminalLog?.Invoke(LocalizationManager.Instance.GetString("OperationCancelledPartialSaved"));

                    }

                    throw; // iptal işleminin yukarıda yakalanması için tekrar fırlat

                }

                assistantMessage = response?.Choices?.FirstOrDefault()?.Message;

                //_terminalLog?.Invoke($"[ChatFlow] Received response. assistantMessage null: {assistantMessage==null}");

                if (response != null)

                {

                    try

                    {

                        var promptTokens = response.Usage?.PromptTokens ?? (messagesToSend.Sum(m => (m.Content?.Length ?? 0)) / 4);

                        var completionTokens = response.Usage?.CompletionTokens ?? (assistantMessage?.Content?.Length ?? 0) / 4;

                        var modelName = _apiClient is OpenAiCompatibleClient oac ? oac.ResolveConnectionSettings().Model : _apiClient.ModelName;
                        TokenTrackerService.Instance.RecordModelRequest(modelName, true, response.TotalLatencyMs);

                        // var logStr = $"\n=== MDAIAGENT LLM REQUEST ===\n" +

                        //              $"Model: {modelName}\n" +

                        //              $"Estimated/Actual Tokens: {promptTokens}\n" +

                        //              $"TTFT: {(response.TtftMs / 1000.0):0.0}s\n" +

                        //              $"Total Latency: {(response.TotalLatencyMs / 1000.0):0.0}s\n" +

                        //              $"Tokens: Prompt {promptTokens} | Completion {completionTokens}\n" +

                        //              $"=============================\n";

                        // _terminalLog?.Invoke(logStr);

                    }

                    catch { }

                }

                // If streaming returned an assistantMessage but its content is empty, retry non-streaming to obtain full text

                if (assistantMessage != null && string.IsNullOrEmpty(assistantMessage.Content) && iteration == 0 && onTokenReceived != null)

                {

                        //_terminalLog?.Invoke("[ChatFlow] Streaming returned empty assistant content — retrying non-streaming call to recover full response...");

                    try

                    {

                        var retryResp = await _apiClient.SendChatWithToolsAsync(messagesToSend, activeTools, cancellationToken);
                        if (retryResp != null)
                        {
                            var retryModelName = _apiClient is OpenAiCompatibleClient retryClient ? retryClient.ResolveConnectionSettings().Model : _apiClient.ModelName;
                            TokenTrackerService.Instance.RecordModelRequest(retryModelName, true, retryResp.TotalLatencyMs, isRetry: true);
                        }

                        var retryMsg = retryResp?.Choices?.FirstOrDefault()?.Message;

                        //_terminalLog?.Invoke($"[ChatFlow] Retry response received. null: {retryMsg==null} contentLen={(retryMsg?.Content?.Length ?? 0)}");

                        if (retryMsg != null && !string.IsNullOrEmpty(retryMsg.Content))

                        {

                            assistantMessage = retryMsg;

                        }

                    }

                    catch (Exception)

                    {
                        var retryModelName = _apiClient is OpenAiCompatibleClient retryClient
                            ? retryClient.ResolveConnectionSettings().Model
                            : _apiClient.ModelName;
                        TokenTrackerService.Instance.RecordModelRequest(retryModelName, false, 0, isRetry: true);
                        //_terminalLog?.Invoke("[ChatFlow] Non-streaming retry failed.");

                    }

                }

                // If streaming was used and assistantMessage has empty Content, use tokenBuffer

                if (assistantMessage != null && string.IsNullOrEmpty(assistantMessage.Content) && tokenBuffer != null)

                {

                    try

                    {

                        var assembled = tokenBuffer.ToString();

                        if (!string.IsNullOrEmpty(assembled))

                        {

                            // Limit assembled size to avoid huge history entries

                            if (assembled.Length > 200000) assembled = assembled.Substring(0, 200000) + "\n\n[TRUNCATED: Çok uzun olduğu için kırpıldı]";

                            assistantMessage.Content = assembled;

                        }

                    }

                    catch { }

                }

                // Final recovery: if still empty after retry and tokenBuffer, try a concise non-streaming re-prompt

                if (assistantMessage != null && string.IsNullOrEmpty(assistantMessage.Content))

                {

                    //_terminalLog?.Invoke("[ChatFlow] assistantMessage still empty — attempting concise recovery non-streaming call...");

                    try

                    {

                        var recoveryMessages = new List<ExtendedChatMessage>(messagesToSend)

                        {

                            new ExtendedChatMessage { Role = "user", Content = LocalizationManager.Instance.GetString("LutfenOncekiIstegeKisaVeOzBirYanitVerSadeceKisaBirMetinIleCevapVeriniz") }

                        };

                        var recResp = await _apiClient.SendChatWithToolsAsync(recoveryMessages, activeTools, cancellationToken);

                        var recMsg = recResp?.Choices?.FirstOrDefault()?.Message;

                        //_terminalLog?.Invoke($"[ChatFlow] Recovery response received. null: {recMsg==null} contentLen={(recMsg?.Content?.Length ?? 0)}");

                        if (recMsg != null && !string.IsNullOrEmpty(recMsg.Content))

                        {

                            assistantMessage = recMsg;

                        }

                    }

                    catch (Exception)

                    {

                        //_terminalLog?.Invoke("[ChatFlow] Recovery call failed.");

                    }

                }

                if (assistantMessage == null)

                {

                    //_terminalLog?.Invoke("[ChatFlow] assistantMessage was null after response.");

                    result.Success = false;

                    result.ErrorMessage = "AI yanıtı alınamadı.";

                    var msg = new ChatFlowMessage { Sender = "AI Asistan", Content = LocalizationManager.Instance.GetString("BosYanitAlindi") };

                    result.Messages.Add(msg);

                    onMessageAdded?.Invoke(msg);

                    break;

                }

                if (!fallbackQuestionsHandled && assistantMessage.ToolCalls == null && _toolExecutor != null)

                {

                    // ⚡ DİNAMİK RE-ROUTING (Option 3): Ana model listede olmayan bir aracı talep ettiyse kataloğu genişlet ve devam et

                    if (!string.IsNullOrEmpty(assistantMessage.Content) && assistantMessage.Content.Contains("[REQUEST_TOOL:"))

                    {

                        var reqMatch = System.Text.RegularExpressions.Regex.Match(assistantMessage.Content, @"\[REQUEST_TOOL:\s*(\w+)\]");

                        if (reqMatch.Success)

                        {

                            var requestedToolName = reqMatch.Groups[1].Value;

                            var reqTool = allActiveTools.FirstOrDefault(t => string.Equals(t.Function?.Name, requestedToolName, StringComparison.OrdinalIgnoreCase));

                            if (reqTool != null && !activeTools.Any(t => string.Equals(t.Function?.Name, requestedToolName, StringComparison.OrdinalIgnoreCase)))

                            {

                                activeTools.Add(reqTool);

                                _terminalLog?.Invoke($"⚡ [Dynamic Re-Routing] Ana model '{requestedToolName}' aracını talep etti. Kataloğa eklendi!");

                                UpdateSystemPromptForSelectedTools(messagesToSend, activeTools, systemPrompt, needsPlan);

                                iteration++;

                                continue;

                            }

                        }

                    }

                    var fallbackQuestions = ExtractFallbackQuestions(assistantMessage.Content);

                    if (fallbackQuestions.Count > 0)

                    {

                        fallbackQuestionsHandled = true;

                        session?.History.Add(assistantMessage);

                        messagesToSend.Add(assistantMessage);

                        onMessageAdded?.Invoke(new ChatFlowMessage { Sender = "AI Asistan", Content = string.Empty });

                        var answers = new List<string>();

                        foreach (var question in fallbackQuestions)

                        {

                            var optionResult = await _toolExecutor.RequestUserOptionsAsync(question.Question, question.Options, question.AllowMultiple);

                            if (!optionResult.Success)

                            {

                                answers.Add($"{question.Question}: kullanıcı seçimi alınamadı");

                            }

                            else

                            {

                                answers.Add($"{question.Question}: {optionResult.Output}");

                            }

                        }

                        var answerMessage = new ExtendedChatMessage

                        {

                            Role = "user",

                            Content = LocalizationManager.Instance.GetString("KullaniciAsagidakiSecimleriYapti") + string.Join("\n", answers)

                        };

                        session?.History.Add(answerMessage);

                        messagesToSend.Add(answerMessage);

                        SaveSessions();

                        _updateOperationStep?.Invoke("✅ Seçimler alındı; AI görevi değerlendiriyor...");

                        iteration++;

                        continue;

                    }

                }

                //_terminalLog?.Invoke($"[ChatFlow] assistantMessage.Role={assistantMessage.Role} contentLen={(assistantMessage.Content?.Length ?? 0)} toolCalls={(assistantMessage.ToolCalls?.Count ?? 0)}");

                session?.History.Add(assistantMessage);

                messagesToSend.Add(assistantMessage);

                SaveSessions();

                if (response?.Usage != null)

                {

                    TokenTrackerService.Instance.AddUsage(_apiClient.ModelName, response.Usage);

                }

                if (!string.IsNullOrEmpty(assistantMessage.Content))

                {

                    string displayContent;

                    if (assistantMessage.ToolCalls != null && assistantMessage.ToolCalls.Count > 0)

                    {

                        // Model araç çağrısından önce bir düşünce metni yazmış.

                        // Zaten <think> içeriyorsa tekrar sarma

                        if (assistantMessage.Content.Contains("<think>"))

                        {

                            displayContent = assistantMessage.Content;

                        }

                        else

                        {

                            displayContent = $"<think>\n{assistantMessage.Content}\n</think>";

                        }

                    }

                    else

                    {

                        displayContent = assistantMessage.Content;

                    }

                    var verificationSig = FormatVerificationSignature(lastVerificationResult);
                    if (!string.IsNullOrWhiteSpace(verificationSig))
                        displayContent += verificationSig;

                    var msg = new ChatFlowMessage { Sender = "AI Asistan", Content = displayContent };

                    result.Messages.Add(msg);

                    onMessageAdded?.Invoke(msg);

                }

                var allChangedFiles = new HashSet<string>();
                var hasBuildToolCall = false;
                var hasTestToolCall = false;

                if (assistantMessage.ToolCalls != null && assistantMessage.ToolCalls.Count > 0)

                {

                    // Streaming'den gelen tool call args JSON olarak geçerli mi kontrol et

                    // Bozuk ise, non-streaming ile tekrar istek at

                    if (iteration == 0 && onTokenReceived != null)

                    {

                        bool argsValid = assistantMessage.ToolCalls.All(tc =>

                        {

                            try { System.Text.Json.JsonDocument.Parse(tc.Function.Arguments); return true; }

                            catch { return false; }

                        });

                        if (!argsValid)

                        {

                            _updateOperationStep?.Invoke("⚠️ Streaming JSON bozuk, non-streaming ile tekrar deneniyor...");

                            // Son eklenen assistant mesajını geri al ve non-streaming ile yeniden dene

                            messagesToSend.RemoveAt(messagesToSend.Count - 1);

                            session?.History.RemoveAt(session.History.Count - 1);

                            var retryResponse = await _apiClient.SendChatWithToolsAsync(messagesToSend, activeTools, cancellationToken);

                            assistantMessage = retryResponse?.Choices?.FirstOrDefault()?.Message;

                            if (assistantMessage == null) { result.Success = false; break; }

                            session?.History.Add(assistantMessage);

                            messagesToSend.Add(assistantMessage);

                            SaveSessions();

                        }

                    }

                    var completedTasks = new List<(string ToolName, ToolResult Result, ExtendedChatMessage ChatMessage, ToolCall ToolCall)>();

                    foreach (var toolCall in assistantMessage.ToolCalls ?? Enumerable.Empty<ToolCall>())

                    {

                        if (toolCall.Function == null)

                            continue;

                        var toolName = toolCall.Function.Name;

                        if (string.IsNullOrWhiteSpace(toolName))

                            continue;

                        var args = toolCall.Function.Arguments ?? "{}";

                        _updateOperationStep?.Invoke($"⚙️ {toolName} çalıştırılıyor...");

                        // Son kullanıcı mesajını bağlam olarak gönder (özellikle CreatePlan için)

                        var lastUserMsg = session?.History?

                            .LastOrDefault(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))?

                            .Content;

                        if (_toolExecutor == null)

                        {

                            _terminalLog?.Invoke($"⚠️ {toolName} için araç çalıştırıcısı mevcut değil.");

                            continue;

                        }

                        if (toolName == "BuildProject")
                        {
                            hasBuildToolCall = true;
                        }

                        if (toolName == "RunTests")
                        {
                            hasTestToolCall = true;
                        }

                        ToolResult toolResult;

                        if (toolName == "CreatePlan" && planCreatedForCurrentTask)

                        {

                            _terminalLog?.Invoke(LocalizationManager.Instance.GetString("PlanAlreadyExistsSkipped"));

                            toolResult = new ToolResult

                            {

                                Success = true,

                                Output = "Bu görev için plan zaten oluşturuldu. Mevcut planı kullanarak uygulamaya devam et; tekrar CreatePlan çağırma."

                            };

                        }

                        else

                        {

                            toolResult = _safeRuntime is { IsProductionReady: true }
                                ? await _safeRuntime.ExecuteToolAsync(toolName, args, cancellationToken, lastUserMsg)
                                : await _toolExecutor.ExecuteAsync(toolName, args, cancellationToken, lastUserMsg);

                            if (toolName == "CreatePlan" && toolResult.Success)

                            {

                                planCreatedForCurrentTask = true;

                            }

                        }

                        // 🛡️ Loop-Guard: aynı tool + aynı argümanlarla üst üste maxSameToolRetry başarısızlık → döngüyü kır
                        if (!string.IsNullOrWhiteSpace(args))
                        {
                            var cacheKey = (toolName, args);
                            if (!toolArgsHashCache.TryGetValue(cacheKey, out var argsHash))
                            {
                                argsHash = args.GetHashCode(StringComparison.Ordinal);
                                toolArgsHashCache[cacheKey] = argsHash;
                            }
                            var streakKey = (ToolName: toolName, ArgsHash: argsHash);

                            if (toolResult.Success)
                            {
                                toolFailureStreak.Remove(streakKey);
                            }
                            else
                            {
                                toolFailureStreak.TryGetValue(streakKey, out var currentStreak);
                                currentStreak++;
                                toolFailureStreak[streakKey] = currentStreak;

                                if (currentStreak >= maxSameToolRetry)
                                {
                                    loopGuardTriggered = true;
                                    loopGuardMessage = $"Döngü koruması tetiklendi: '{toolName}' aracı aynı argümanlarla üst üste {currentStreak} kez başarısız oldu. Sonsuz döngüden kaçınmak için işlem durduruldu. Lütfen yaklaşımı değiştirin veya ilgili dosyayı ReadFile ile güncel okuyun.";
                                    _terminalLog?.Invoke($"🛡️ {loopGuardMessage}");
                                }
                            }
                        }

                        if (loopGuardTriggered)

                        {

                            var loopGuardToolMsg = new ExtendedChatMessage

                            {

                                Role = "tool",

                                ToolCallId = toolCall.Id,

                                Content = loopGuardMessage

                            };

                            completedTasks.Add((toolName, new ToolResult { Success = false, Error = loopGuardMessage }, loopGuardToolMsg, toolCall));

                            continue;

                        }

                        if (toolResult.Success && IsCodeModificationTool(toolName))

                        {

                            var changedFiles = ExtractChangedFilesFromToolOutput(toolResult.Output ?? "", toolName);

                            foreach (var f in changedFiles) 

                            {

                                allChangedFiles.Add(f);

                                FileModifiedByAi?.Invoke(f);

                            }

                        }

                        if (toolResult.Output != null && toolResult.Output.Length > 12000)

                        {

                            var origLen = toolResult.Output.Length;

                            toolResult.Output = toolResult.Output.Substring(0, 12000)

                                + $"\n\n[⚠️ Çıktı kırpıldı: {origLen:N0} karakterden 12.000'e indirildi. "

                                + "Daha fazlasına ihtiyaç duyuyorsan ReadFile ile startLine/endLine parametrelerini kullan.]";

                        }

                        var toolContent = toolResult.Success

                            ? toolResult.Output

                            : FormatToolErrorMessage(toolResult.Error, toolResult.Output);

                        var toolChatMessage = new ExtendedChatMessage

                        {

                            Role = "tool",

                            ToolCallId = toolCall.Id,

                            Content = toolContent

                        };

                        if (toolName == "TakeScreenshot" && toolResult.Success)

                        {

                            var filePath = ExtractPathFromToolOutput(toolContent ?? string.Empty);

                            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))

                            {

                                try

                                {

                                    var bytes = File.ReadAllBytes(filePath);

                                    var base64 = Convert.ToBase64String(bytes);

                                    toolChatMessage.Attachments = new List<Attachment>

                                    {

                                        new Attachment { FileName = Path.GetFileName(filePath), Base64Content = base64, MimeType = "image/jpeg" }

                                    };

                                }

                                catch { }

                            }

                        }

                        completedTasks.Add((toolName, toolResult, toolChatMessage, toolCall));

                    }

                    // 🔍 Code modification tool'larından sonra verification loop'u SADECE BİR KERE çalıştır

                    var shouldBlockFinalCompletion = ShouldBlockFinalCompletion(
                        message,
                        selectedFolder,
                        hasBuildToolCall,
                        hasTestToolCall,
                        true,
                        allChangedFiles.Count > 0);

                    if (allChangedFiles.Count > 0 && _verificationService != null && !skipVerification)

                    {

                        _updateOperationStep?.Invoke("🔍 Tüm kod değişiklikleri doğrulanıyor (build + test)...");

                        var verificationStopwatch = System.Diagnostics.Stopwatch.StartNew();
                        try

                        {

                            var verificationResult = _safeRuntime is { IsProductionReady: true }
                                ? await _safeRuntime.VerifyChangesAsync(
                                    selectedFolder ?? "",
                                    allChangedFiles.ToList(),
                                    "Verify code changes made by AI. Build must succeed and all tests must pass.",
                                    "Please verify the aggregated changes from the recent tool operations.")
                                : await _verificationService.VerifyChangesAsync(

                                selectedFolder ?? "",

                                allChangedFiles.ToList(),

                                "Verify code changes made by AI. Build must succeed and all tests must pass.",

                                "Please verify the aggregated changes from the recent tool operations."

                                );

                            verificationStopwatch.Stop();
                            TokenTrackerService.Instance.RecordVerification(verificationResult, verificationStopwatch.ElapsedMilliseconds);
                            lastVerificationResult = verificationResult;
                            result.LastVerificationResult = verificationResult;

                            if (!verificationResult.Success)
                            {
                                _terminalLog?.Invoke(LocalizationManager.Instance.GetString("VerificationBasarisiz").Replace("{summary}", verificationResult.Summary));
                                if (shouldBlockFinalCompletion)
                                {
                                    result.Messages.Add(new ChatFlowMessage
                                    {
                                        Sender = "Sistem",
                                        Content = "Görev doğrulanamadı: build/test kontrolü başarısız olduğu için final açıklama verilmedi. Hata düzeltilip doğrulama tekrar çalıştırılmalıdır."
                                    });
                                    break;
                                }
                            }
                            else
                            {
                                _terminalLog?.Invoke(LocalizationManager.Instance.GetString("VerificationBasarili").Replace("{summary}", verificationResult.Summary));
                            }
                        }
                        catch (Exception ex)
                        {
                            _terminalLog?.Invoke($"⚠️ Verification exception: {ex.Message}");
                        }

                    }

                    foreach(var taskRes in completedTasks)

                    {

                        var historyMsg = taskRes.ChatMessage;

                        // History Compression (Phase 2)

                        // Local model için çok büyük output üreten toolları geçmişte özet olarak tut

                        if (isLocalModel && taskRes.Result.Success)

                        {

                            var compressedContent = taskRes.ToolName switch

                            {

                                "ListDirectory" => $"[Araç Başarılı: Klasör içeriği listelendi (Token tasarrufu için detaylar geçmişte gizlendi)]",

                                "ReadFile" => $"[Araç Başarılı: Dosya okundu (Token tasarrufu için detaylar geçmişte gizlendi)]",

                                "CreateOrUpdateFile" => $"[Araç Başarılı: Dosya başarıyla kaydedildi/güncellendi]",

                                "SearchCode" => $"[Araç Başarılı: Arama yapıldı (Detaylar gizlendi)]",

                                "ExecuteTerminalCommand" => $"[Araç Başarılı: Terminal komutu çalıştırıldı]",

                                _ => taskRes.ChatMessage.Content

                            };

                            if (compressedContent != taskRes.ChatMessage.Content)

                            {

                                historyMsg = new ExtendedChatMessage

                                {

                                    Role = taskRes.ChatMessage.Role,

                                    ToolCallId = taskRes.ChatMessage.ToolCallId,

                                    Content = compressedContent,

                                    Attachments = taskRes.ChatMessage.Attachments

                                };

                            }

                        }

                        //_terminalLog?.Invoke($"[ChatFlow] Tool result added to history for {taskRes.ToolName}. Success={taskRes.Result.Success}");

                        session?.History.Add(historyMsg);

                        messagesToSend.Add(taskRes.ChatMessage); // Current turn'e FULL RESULT gider!

                        if (taskRes.Result.Success)

                        {

                            string msgContent = LocalizationManager.Instance.GetString("TaskResToolNameTamamlandi").Replace("{taskRes.ToolName}", taskRes.ToolName);

                            if (taskRes.ToolName == "CreateOrUpdateFile" || taskRes.ToolName == "ReplaceFileContent")

                            {

                                var filePath = ExtractPathFromToolOutput(taskRes.Result.Output);

                                if (!string.IsNullOrEmpty(filePath))

                                {

                                    result.UpdatedFilePaths.Add(filePath);

                                    var fileName = Path.GetFileName(filePath);

                                    msgContent = $"✅ {taskRes.ToolName}: 📄 {fileName} kaydedildi";

                                    // Dosya ağacının otomatik yenilenmesi için FileChangeEvent yayınla

                                    var changeType = taskRes.ToolName == "CreateOrUpdateFile"

                                        ? FileChangeType.Created

                                        : FileChangeType.Modified;

                                    EventBus.Publish(new FileChangeEvent

                                    {

                                        FilePath = filePath,

                                        Type = changeType

                                    });

                                }

                                else

                                {

                                    msgContent = LocalizationManager.Instance.GetString("TaskResToolNameTamamlandi").Replace("{taskRes.ToolName}", taskRes.ToolName);

                                }

                            }

                            else if (taskRes.ToolName == "DeleteFile")

                            {

                                var filePath = ExtractPathFromToolOutput(taskRes.Result.Output);

                                if (!string.IsNullOrEmpty(filePath))

                                {

                                    EventBus.Publish(new FileChangeEvent

                                    {

                                        FilePath = filePath,

                                        Type = FileChangeType.Deleted

                                    });

                                }

                                msgContent = LocalizationManager.Instance.GetString("TaskResToolNameTamamlandi").Replace("{taskRes.ToolName}", taskRes.ToolName);

                            }

                            var msg = new ChatFlowMessage { Sender = "Sistem", Content = msgContent, ShouldRefreshFileTree = result.UpdatedFilePaths.Count > 0 };

                                                        result.Messages.Add(msg);

                                                        // onMessageAdded çağrılmaz - sistem mesajları Timeline panelinde EventBus üzerinden görünür

                                                    }

                                                    else

                                                    {

                                                        result.Success = false;

                            var msg = new ChatFlowMessage { Sender = "Sistem", Content = LocalizationManager.Instance.GetString("TaskResToolNameAracindaBirHataOlustuAIHatayiInceleyipDuzeltmeDenemesiYapiyor").Replace("{taskRes.ToolName}", taskRes.ToolName) };

                            result.Messages.Add(msg);

                            onMessageAdded?.Invoke(msg);

                            _notify?.Invoke($"Araç hatası: {taskRes.ToolName}", NotificationSeverity.Error);

                        }

                    }

                    SaveSessions();

                    // 🛡️ Loop-Guard tetiklendiyse sonsuz döngüyü kır
                    if (loopGuardTriggered && !string.IsNullOrWhiteSpace(loopGuardMessage))
                    {
                        result.Messages.Add(new ChatFlowMessage
                        {
                            Sender = "Sistem",
                            Content = loopGuardMessage
                        });
                        result.Success = false;
                        break;
                    }

                    iteration++;

                }

                else

                {

                    if (planCreatedForCurrentTask && planContinuationAttempts < 2)
                    {
                        planContinuationAttempts++;
                        var continuationMessage = new ExtendedChatMessage
                        {
                            Role = "user",
                            Content = "Plan oluşturuldu ancak uygulama adımları henüz tamamlanmadı. Mevcut planı kullanarak dosya oluşturma/düzenleme, build ve test adımlarına devam et; yalnızca gerçekten tamamlandıktan sonra kısa final yanıt ver."
                        };
                        session?.History.Add(continuationMessage);
                        messagesToSend.Add(continuationMessage);
                        _terminalLog?.Invoke($"ℹ️ Plan sonrası uygulamaya devam ediliyor ({planContinuationAttempts}/2)...");
                        iteration++;
                        continue;
                    }

                    // No tool calls, AI is done

                    // If streaming was used (onTokenReceived != null) the tokens may have been delivered via onTokenReceived.

                    // However some servers don't stream; ensure we still add the final assistant content to the chat.

                    // UI bildirimi artık iterasyonun başında yapılıyor

                    try

                    {

                        var finalContent = assistantMessage.Content ?? string.Empty;

                        if (string.IsNullOrEmpty(finalContent))

                        {

                            // No content received — log for debugging

                            //_terminalLog?.Invoke("[ChatFlow] Uyarı: assistantMessage içerik boş (streaming veya model yanıtı eksik).");

                        }

                    }

                    catch (Exception)

                    {

                        //_terminalLog?.Invoke("[ChatFlow] onMessageAdded hata.");

                    }

                    var hasProjectTaskIntent = ShouldBlockFinalCompletion(message, selectedFolder, hasBuildToolCall, hasTestToolCall, false, allChangedFiles.Count > 0);

                    if (hasProjectTaskIntent && _verificationService != null && !skipVerification)
                    {
                        var pendingVerificationMessage = new ChatFlowMessage
                        {
                            Sender = "Sistem",
                            Content = "Görev için proje doğrulaması gereklidir. Build/test kontrolü tamamlanmadan final tamamlandı sinyali verilmez."
                        };
                        result.Messages.Add(pendingVerificationMessage);
                        _updateOperationStep?.Invoke("⏳ Doğrulama bekleniyor...");
                        break;
                    }

                    _updateOperationStep?.Invoke("✅ İşlem tamamlandı");

                    break;

                }

            }

            if (iteration >= maxIterations)

            {

                 result.Messages.Add(new ChatFlowMessage { Sender = "Sistem", Content = LocalizationManager.Instance.GetString("IslemDonguLimitineUlastiVeDurduruldu") });

            }

            // Run Agent Pipeline

            if (qaAgentEnabled || uiAgentEnabled)

            {

                string? finalContent = assistantMessage?.Content;

                if (!string.IsNullOrEmpty(finalContent))

                {

                    // QA Agent

                    if (qaAgentEnabled)

                    {

                        _updateOperationStep?.Invoke("🔍 QA Denetim Ajanı çalışıyor...");

                        var qaMsg = new ChatFlowMessage { Sender = "Sistem", Content = LocalizationManager.Instance.GetString("QADenetimAjaniDevrede") };

                        result.Messages.Add(qaMsg);

                        onMessageAdded?.Invoke(qaMsg);

                        finalContent = await RunAgentPipelineStepAsync(

                            finalContent,

                            "Sen bir kod denetim uzmanısın. Aşağıdaki kodu incele: olası hatalar, eksik edge case'ler, güvenlik sorunları ve iyileştirmeler olup olmadığını kontrol et. Sadece düzeltilmiş kodu kod bloğu içinde ver, ek açıklama yapma.",

                            selectedFolder,

                            cancellationToken);

                        var qaDoneMsg = new ChatFlowMessage { Sender = "Sistem", Content = LocalizationManager.Instance.GetString("QADenetimAjaniTamamlandi") };

                        result.Messages.Add(qaDoneMsg);

                        onMessageAdded?.Invoke(qaDoneMsg);

                    }

                    // UI & Refactor Agent

                    if (uiAgentEnabled)

                    {

                        _updateOperationStep?.Invoke("🎨 UI & Refaktör Ajanı çalışıyor...");

                        var uiMsg = new ChatFlowMessage { Sender = "Sistem", Content = LocalizationManager.Instance.GetString("UIRefaktorAjaniDevrede") };

                        result.Messages.Add(uiMsg);

                        onMessageAdded?.Invoke(uiMsg);

                        finalContent = await RunAgentPipelineStepAsync(

                            finalContent,

                            "Sen bir kod temizleme ve UI geliştirme uzmanısın. Aşağıdaki kodu al: kod temizliği yap, daha okunabilir hale getir, eğer UI kodu varsa iyileştir. Sadece son hali kod bloğu içinde ver.",

                            selectedFolder,

                            cancellationToken);

                        var uiDoneMsg = new ChatFlowMessage { Sender = "Sistem", Content = LocalizationManager.Instance.GetString("UIRefaktorAjaniTamamlandi") };

                        result.Messages.Add(uiDoneMsg);

                        onMessageAdded?.Invoke(uiDoneMsg);

                    }

                    var verificationSig = FormatVerificationSignature(lastVerificationResult);
                    if (!string.IsNullOrWhiteSpace(verificationSig))
                        finalContent += verificationSig;

                    // Update the final assistant message with the agent result

                    if (session != null && session.History.Count > 0 && session.History.Last().Role == "assistant")

                    {

                        session.History.Last().Content = finalContent;

                        SaveSessions();

                    }

                    // Also send the final content as an AI message to the chat

                    var finalMsg = new ChatFlowMessage { Sender = "AI Asistan", Content = finalContent };

                    result.Messages.Add(finalMsg);

                    onMessageAdded?.Invoke(finalMsg);

                }

            }

            return result;

        }

        catch (Exception ex)

        {
            if (_apiClient != null)
            {
                var failedModelName = _apiClient is OpenAiCompatibleClient failedClient
                    ? failedClient.ResolveConnectionSettings().Model
                    : _apiClient.ModelName;
                TokenTrackerService.Instance.RecordModelRequest(failedModelName, false, 0);
            }

            var errorMessage = $"Hata: {ex.Message}";

            var errorResult = new ChatFlowResult

            {

                Success = false,

                ErrorMessage = ex.Message,

                Messages = new List<ChatFlowMessage>

                {

                    new ChatFlowMessage { Sender = "AI Asistan", Content = errorMessage }

                }

            };

            onMessageAdded?.Invoke(new ChatFlowMessage { Sender = "AI Asistan", Content = errorMessage });

            return errorResult;

        }

    }

    private static string FormatVerificationSignature(VerificationResult? vr)
    {
        if (vr == null || vr.BuildTestResults.Count == 0)
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine();
        sb.AppendLine("---");
        sb.Append("🧪 Doğrulama: ");

        var fileCount = vr.ChangedFiles.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (fileCount > 0)
            sb.Append($"{fileCount} dosya · ");

        int passed = 0, failed = 0, warnings = 0;
        foreach (var step in vr.BuildTestResults)
        {
            if (step.Success)
            {
                var isWarn = !string.IsNullOrWhiteSpace(step.Error) &&
                    (step.Error.Contains("uyarı", StringComparison.OrdinalIgnoreCase) ||
                     step.Error.Contains("warn", StringComparison.OrdinalIgnoreCase) ||
                     step.Error.Contains("warning", StringComparison.OrdinalIgnoreCase) ||
                     step.Error.Contains("CS", StringComparison.OrdinalIgnoreCase));
                if (isWarn) warnings++;
                else passed++;
            }
            else failed++;
        }

        if (failed > 0) sb.Append("❌");
        else if (warnings > 0) sb.Append("⚠️");
        else sb.Append("✅");
        sb.Append($" {passed} başarılı");
        if (warnings > 0) sb.Append($", {warnings} uyarı");
        if (failed > 0) sb.Append($", {failed} başarısız");
        sb.AppendLine();

        foreach (var step in vr.BuildTestResults)
        {
            var isWarn = step.Success && !string.IsNullOrWhiteSpace(step.Error) &&
                (step.Error.Contains("uyarı", StringComparison.OrdinalIgnoreCase) ||
                 step.Error.Contains("warn", StringComparison.OrdinalIgnoreCase) ||
                 step.Error.Contains("warning", StringComparison.OrdinalIgnoreCase) ||
                 step.Error.Contains("CS", StringComparison.OrdinalIgnoreCase));

            string symbol = step.Success ? (isWarn ? "⚠️" : "✓") : "✕";
            sb.AppendLine($"{symbol} {step.Step}");
        }

        if (!string.IsNullOrWhiteSpace(vr.ErrorSummary))
        {
            var trimmed = vr.ErrorSummary.Trim();
            if (trimmed.Length > 300) trimmed = trimmed.Substring(0, 300) + "...";
            sb.AppendLine($"📝 {trimmed}");
        }

        return sb.ToString();
    }

    private static List<(string Question, List<string> Options, bool AllowMultiple)> ExtractFallbackQuestions(string? content)

    {

        var questions = new List<(string Question, List<string> Options, bool AllowMultiple)>();

        if (string.IsNullOrWhiteSpace(content)) return questions;

        var lines = content.Replace("\r", string.Empty).Split('\n');

        for (int i = 0; i < lines.Length; i++)

        {

            var line = lines[i].Trim();

            // Markdown bold (**), italic (*), veya tire (-) işaretlerini, ardından rakam ve nokta gelen kalıpları eşle

            var match = Regex.Match(line, @"^(?:\*\*|\*|-|\s)*\d+\.(?:\*\*|\*|\s)*(?<question>.+)$");

            if (!match.Success) continue;

            // Kalan metindeki fazlalık markdown yıldızlarını temizle

            var question = match.Groups["question"].Value.Replace("**", "").Replace("*", "").Trim();

            if (!question.Contains('?')) continue;

            var options = new List<string>();

            // Alt satırlardaki maddeleri (bullet points) kontrol et

            int j = i + 1;

            while (j < lines.Length)

            {

                var nextLine = lines[j].Trim();

                if (string.IsNullOrEmpty(nextLine)) { j++; continue; } // Boş satırları atla

                // Eğer yeni bir soru (rakamla başlayan) geldiyse dur

                if (Regex.IsMatch(nextLine, @"^(?:\*\*|\*|-|\s)*\d+\.")) break;

                // Madde imi (-, *, a), b)) ile başlıyorsa seçenek olarak ekle

                var optMatch = Regex.Match(nextLine, @"^(?:-|\*|\w+\)|\(\w+\))\s*(.+)");

                if (optMatch.Success)

                {

                    options.Add(optMatch.Groups[1].Value.Trim());

                    j++;

                }

                else

                {

                    // Ne madde imi ne de boş satırsa (başka bir açıklama falan), seçenek listesi bitmiş demektir

                    break;

                }

            }

            // Alt maddelerden seçenek bulunamadıysa metin içinden (mu yoksa / veya) ayıklamaya çalış

            if (options.Count == 0)

            {

                var alternatives = Regex.Match(question, @"(?<first>.+?)\s+mu\s+yoksa\s+(?<second>.+?)(?:\s+mı|\s+mi|\?)", RegexOptions.IgnoreCase);

                if (alternatives.Success)

                {

                    options.Add(alternatives.Groups["first"].Value.Trim(' ', ',', ':'));

                    options.Add(alternatives.Groups["second"].Value.Trim(' ', ',', ':', '?'));

                }

                else

                {

                    alternatives = Regex.Match(question, @"(?<first>.+?)\s+veya\s+(?<second>.+?)(?:\?)$", RegexOptions.IgnoreCase);

                    if (alternatives.Success)

                    {

                        options.Add(alternatives.Groups["first"].Value.Trim(' ', ',', ':'));

                        options.Add(alternatives.Groups["second"].Value.Trim(' ', ',', ':', '?'));

                    }

                }

            }

            if (options.Count == 0)

            {

                options.Add(LocalizationManager.Instance.GetString("VarsayilanSeninSectigin"));

                options.Add(LocalizationManager.Instance.GetString("KendimBelirleyecegim"));

            }

            questions.Add((question, options, false));

            i = j - 1; // Dış döngüyü seçeneklerden sonrasına ilerlet

        }

        return questions;

    }

    public void SaveSessions()

    {

        _sessionService.SaveSessions(ChatSessions);

    }

    public static List<ExtendedChatMessage> TrimHistoryForEdit(IList<ExtendedChatMessage> history, int editIndex)

    {

        if (history == null)

        {

            return new List<ExtendedChatMessage>();

        }

        if (editIndex <= 0)

        {

            return new List<ExtendedChatMessage>();

        }

        var safeIndex = Math.Max(0, Math.Min(editIndex, history.Count));

        return history.Take(safeIndex).ToList();

    }

    private static string FormatToolErrorMessage(string? error, string? output)

    {

        if (string.IsNullOrWhiteSpace(error))

        {

            return "Hata bilgisi alınamadı. Lütfen terminal çıktısını kontrol edin.";

        }

        var summary = error.Trim();

        if (summary.Length > 1600)

        {

            summary = summary.Substring(0, 1600) + "...";

        }

        if (!string.IsNullOrWhiteSpace(output))

        {

            var outputLines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)

                                    .Select(l => l.Trim())

                                    .Where(l => !string.IsNullOrWhiteSpace(l))

                                    .Take(3);

            var outputPreview = string.Join(" | ", outputLines);

            if (!string.IsNullOrEmpty(outputPreview))

            {

                summary += $" (Çıktı önizlemesi: {outputPreview})";

            }

        }

        return summary;

    }

    private static bool IsContinuationMessage(string message)
    {
        var normalized = message.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return false;

        var lower = normalized.ToLowerInvariant();
        var continuationPhrases = new[]
        {
            "devam", "devam et", "devam edelim", "işleme devam", "işleme devam et",
            "continue", "continue please", "keep going", "go ahead", "continue task",
            "proceed", "proceed with task", "tamam devam", "devam ediyoruz"
        };

        if (lower.Length <= 40 && continuationPhrases.Any(phrase => lower.Contains(phrase)))
            return true;

        var shortContinuationPatterns = new[]
        {
            "devam ed", "devam et", "devam", "continue", "proceed"
        };

        if (lower.Length <= 12 && shortContinuationPatterns.Any(phrase => lower == phrase || lower.StartsWith(phrase)))
            return true;

        return false;
    }

    private static bool IsCasualMessage(string message)
    {
        var normalized = message.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized)) return false;

        if (IsContinuationMessage(message))
            return false;

        var casualPhrases = new[]
        {
            "merhaba", "selam", "hello", "hi", "hey", "nasılsın", "nasilsin",
            "iyi misin", "teşekkürler", "tesekkurler", "sağ ol", "sag ol"
        };

        return normalized.Length <= 40 && casualPhrases.Any(phrase => normalized.Contains(phrase));
    }

    private static bool ShouldBlockFinalCompletion(string userMessage, string? projectFolder, bool hasBuildToolCall, bool hasTestToolCall, bool hasVerificationRun, bool hasProjectArtifacts)
    {
        if (string.IsNullOrWhiteSpace(userMessage) || string.IsNullOrWhiteSpace(projectFolder))
            return false;

        var lower = userMessage.ToLowerInvariant();
        var projectIntentKeywords = new[]
        {
            "oluştur", "create", "geliştir", "develop", "kur", "setup",
            "proje", "project", "uygulama", "app", "component", "test et",
            "build", "derle", "run tests", "testleri çalıştır", "çalıştır"
        };

        var isProjectTask = projectIntentKeywords.Any(keyword => lower.Contains(keyword));
        if (!isProjectTask)
            return false;

        return hasBuildToolCall || hasTestToolCall || hasProjectArtifacts || !hasVerificationRun;
    }

    /// <summary>

    /// Yerel modeller için LLM çağrısı yapmadan görev karmaşıklığını tahmin eder.

    /// Kural tabanlı heuristik: ~0ms, 0 token maliyet.

    /// </summary>

    private static bool IsComplexTaskHeuristic(string message)

    {

        var lower = message.ToLowerInvariant();

        // Yeni uygulama / sistem / proje oluşturma → kesinlikle KOMPLEKS

        var complexTriggers = new[]

        {

            "yap ", "yap.", "yap!", "oluştur", "geliştir", "yaz ", "yaz.", "kur ", "kur.",

            "ekle ve", "implement", "create", "build", "setup", "entegre",

            "uygulama", "sistem", "proje", "web sitesi", "web site", "mobil",

            "api", "veritabanı", "database", "servis", "microservice",

            "mimari", "architecture", "refactor", "yeniden yaz", "sıfırdan"

        };

        // Basit düzeltme / küçük değişiklik → BASİT

        var simpleTriggers = new[]

        {

            "düzelt ", "düzelt.", "düzelt!", "değiştir", "güncelle", "rename",

            "yeniden adlandır", "sil ", "kaldır ", "hide", "gizle",

            "renk", "font", "boyut", "margin", "padding", "stil", "style",

            "yazım hatası", "typo", "bug fix", "hata düzelt"

        };

        foreach (var trigger in simpleTriggers)

            if (lower.Contains(trigger)) return false;

        foreach (var trigger in complexTriggers)

            if (lower.Contains(trigger)) return true;

        // Uzun ve belirsiz → güvenli taraf: KOMPLEKS say, plan yapsın

        return message.Length > 80;

    }

    private static List<ToolDefinition> GetRouterFallbackTools(IEnumerable<ToolDefinition> availableTools)
    {
        var fallbackNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ReadFile",
            "FindFiles",
            "SearchCode",
            "ListDirectory",
            "DiscoverProjectContext",
            "CreateOrUpdateFile",
            "ReplaceFileContent",
            "BuildProject",
            "RunTests"
        };

        return ToolCapabilityScoring.OrderByScore(
            availableTools.Where(tool => tool.Function?.Name != null && fallbackNames.Contains(tool.Function.Name)))
            .ToList();
    }

    private static void UpdateSystemPromptForSelectedTools(
        List<ExtendedChatMessage> messages,
        IEnumerable<ToolDefinition> selectedTools,
        string systemPrompt,
        bool needsPlan)
    {
        var selectedNames = selectedTools
            .Where(tool => tool.Function?.Name != null)
            .Select(tool => tool.Function!.Name!);
        var mode = SettingsWindow.GetSettings().ActiveWorkspaceMode;
        var dynamicCore = ToolRegistry.GetCoreSystemPrompt(mode) + "\n\n" + ToolRegistry.GetToolCatalogPrompt(selectedNames);
        var dynamicPrompt = string.IsNullOrWhiteSpace(systemPrompt)
            ? dynamicCore
            : dynamicCore + "\n\n---\n[KULLANICI KİŞİSELLEŞTİRMESİ - Bu talimatlara uy, ancak araç kullanımı ve güvenlik kurallarını asla atlatma]\n" + systemPrompt;

        if (needsPlan)
        {
            dynamicPrompt += "\n\n<planning_instruction>\nBefore implementation, briefly outline the required steps in your mind, then proceed directly with execution. Do NOT ask the user for a plan review — just implement.\n</planning_instruction>";
        }

        dynamicPrompt += "\n\n" + ResponseLanguageInstruction;

        var systemMessage = messages.FirstOrDefault(message => message.Role == "system");
        if (systemMessage != null)
        {
            systemMessage.Content = dynamicPrompt;
        }
    }

    private List<ExtendedChatMessage> BuildMessages(string message, string? currentFilePath, string? currentFileContent, string? selectedFolder, string systemPrompt, List<ToolDefinition> activeTools, bool isLocalModel = false)

    {

        // Çekirdek sistem promptu + araç kataloğu HER ZAMAN dahil edilir.

        // Kullanıcının kişiselleştirmesi bunun ÜSTÜNE eklenir, yerine geçmez.

        string finalSystemPrompt;

        var activeToolNames = activeTools.Select(t => t.Function!.Name!);

        var mode = SettingsWindow.GetSettings().ActiveWorkspaceMode;
        string corePrompt = ToolRegistry.GetCoreSystemPrompt(mode) + "\n\n" + ToolRegistry.GetToolCatalogPrompt(activeToolNames);

        // Kullanıcı kişiselleştirmesi varsa çekirdeğin ALTINA ekle

        if (!string.IsNullOrEmpty(systemPrompt))

        {

            finalSystemPrompt = corePrompt

                + "\n\n---\n[KULLANICI KİŞİSELLEŞTİRMESİ - Bu talimatlara uy, ancak araç kullanımı ve güvenlik kurallarını asla atlatma]\n"

                + systemPrompt;

        }

        else

        {

            finalSystemPrompt = corePrompt;

        }

        // "Devre dışı araçlar" uyarısı SADECE manuel araç yönetimi aktifken eklenir.

        // Router aktifken bu liste gönderilmez — Router zaten araçları dinamik seçiyor,

        // hem "kullan bu araçları" hem de "bu araçlar devre dışı" demek çelişkiye yol açar.

        var appSettings = SettingsWindow.GetSettings();

        if (appSettings.IsManualToolManagement)

        {

            var disabledTools = appSettings.DisabledTools;

            if (disabledTools != null && disabledTools.Any())

            {

                finalSystemPrompt += $"\n\n[Devre dışı araçlar: {string.Join(", ", disabledTools)}]\n" +

                    "Kullanıcı bu araçları kapatmış. Eğer bir görevi yapmak için bu araçlardan birine " +

                    "kesinlikle ihtiyacın varsa, kullanıcıya 'Araçlar menüsünden [araç adı] aracını açınız' şeklinde bilgi ver.";

            }

        }

        if (!string.IsNullOrEmpty(selectedFolder))

        {

            // [A] Constitution Cache: aynı klasör + 30 saniye içindeyse diskten okuma

            bool cacheValid = _cachedConstitutionFolder == selectedFolder

                              && _cachedSystemPrompt != null

                              && (DateTime.Now - _constitutionCacheTime) < ConstitutionCacheDuration;

            if (cacheValid)

            {

                finalSystemPrompt = _cachedSystemPrompt!;

            }

            else

            {

                var constitutionPath = Path.Combine(selectedFolder, ".mdai", "constitution.md");

                if (File.Exists(constitutionPath))

                {

                    try

                    {

                        var constitutionContent = File.ReadAllText(constitutionPath);

                        if (!string.IsNullOrWhiteSpace(constitutionContent))

                        {

                            finalSystemPrompt += $"\n\n[PROJE KURALLARI VE ANAYASASI (CONSTITUTION)]\nAşağıdaki kurallar bu proje için özel olarak tanımlanmıştır ve diğer her şeyden daha önceliklidir. Mutlaka bu kurallara uyarak çalış:\n\n{constitutionContent}";

                        }

                    }

                    catch { }

                }

                // Cache'e al

                _cachedSystemPrompt = finalSystemPrompt;

                _cachedConstitutionFolder = selectedFolder;

                _constitutionCacheTime = DateTime.Now;

            }

        }

        if (!finalSystemPrompt.Contains("<response_language>", StringComparison.Ordinal))
            finalSystemPrompt += "\n\n" + ResponseLanguageInstruction;

            var systemBuilder = new StringBuilder();

        systemBuilder.AppendLine(finalSystemPrompt.Trim());

        if (!string.IsNullOrEmpty(selectedFolder))

        {

            systemBuilder.AppendLine();

            systemBuilder.AppendLine("[Aktif Proje Dizini]");

            systemBuilder.AppendLine($"Yol: {selectedFolder}");

            systemBuilder.AppendLine("Proje genelinde çalış. Açık olmayan dosyalar da dahil olmak üzere gerekli dosyaları FindFiles/SearchCode ile bul. Düzenleme gerekiyorsa ilgili dosyaya gidip değişiklik yap.");

        }

        if (!string.IsNullOrEmpty(currentFilePath) && !string.IsNullOrEmpty(currentFileContent))

        {

            systemBuilder.AppendLine();

            systemBuilder.AppendLine("[Aktif Düzenleyici Bağlamı]");

            systemBuilder.AppendLine($"Açık Dosya: {Path.GetFileName(currentFilePath)}");

            systemBuilder.AppendLine($"Yol: {currentFilePath}");

            if (isLocalModel && currentFileContent.Length > 800)

            {

                // Yerel model için dosya içeriğini gizle – sadece metadata ver.

                // Bu, prompt boyutunu büyük oranda küçülterek TTFT süresini saniyeler içine çeker.

                var lineCount = currentFileContent.Count(c => c == '\n') + 1;

                var charCount = currentFileContent.Length;

                systemBuilder.AppendLine($"Satır Sayısı: {lineCount} | Boyut: {charCount:N0} karakter");

                systemBuilder.AppendLine("[Bilgi: Dosya içeriği token tasarrufu için gizlendi. İçeriği görmek için ReadFile aracını kullanabilirsiniz.]");

            }

            else

            {

                systemBuilder.AppendLine("İçerik:");

                systemBuilder.AppendLine("```");

                systemBuilder.AppendLine(currentFileContent);

                systemBuilder.AppendLine("```");

            }

        }

        // [B] Token Bazlı History Budget — mesaj sayısı yerine karakter (≈token) bütçesi

        var nonSystemHistory = ActiveSession!.History.Where(m => m.Role != "system").ToList();

        int charBudget = (isLocalModel ? LocalModelHistoryTokenBudget : CloudModelHistoryTokenBudget) * 4;

        int totalChars = 0;

        int cutIndex = nonSystemHistory.Count;

        for (int i = nonSystemHistory.Count - 1; i >= 0; i--)

        {

            totalChars += (nonSystemHistory[i].Content?.Length ?? 0) + 20; // +20 role overhead

            if (totalChars > charBudget) { cutIndex = i + 1; break; }

        }

        if (cutIndex < nonSystemHistory.Count)

        {

            int dropped = cutIndex;

            systemBuilder.AppendLine();

            systemBuilder.AppendLine($"[Not: Bu sohbetin {nonSystemHistory.Count} mesajlık geçmişi var. Token bütçesi ({(isLocalModel ? LocalModelHistoryTokenBudget : CloudModelHistoryTokenBudget):N0}) aşıldığı için yalnızca son {nonSystemHistory.Count - dropped} mesaj gönderildi.]");

            nonSystemHistory = nonSystemHistory.Skip(cutIndex).ToList();

        }

        var messages = new List<ExtendedChatMessage>

        {

            new ExtendedChatMessage

            {

                Role = "system",

                Content = systemBuilder.ToString().Trim()

            }

        };

        foreach (var msg in nonSystemHistory)

            messages.Add(msg);

        return messages;

    }

    private string ExtractPathFromToolOutput(string output)

        {

            var firstLine = output?.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

            if (string.IsNullOrEmpty(firstLine))

                return string.Empty;

            const string marker = "SAVED_PATH:";

            if (firstLine.StartsWith(marker))

            {

                return firstLine.Substring(marker.Length).Trim();

            }

            return string.Empty;

        }

        // Tool argümanlarından belirtilen anahtarın değerini JSON'dan çıkar

        private static string ExtractArgValue(string argumentsJson, string key)

        {

            if (string.IsNullOrEmpty(argumentsJson))

                return string.Empty;

            try

            {

                using var doc = System.Text.Json.JsonDocument.Parse(argumentsJson);

                if (doc.RootElement.TryGetProperty(key, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String)

                {

                    return value.GetString() ?? string.Empty;

                }

                return string.Empty;

            }

            catch

            {

                return string.Empty;

            }

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

    private async Task<string> RunAgentPipelineStepAsync(

        string content,

        string systemPrompt,

        string? selectedFolder,

        CancellationToken cancellationToken)

    {

        if (_apiClient == null)

            return content;

        var messages = new List<ExtendedChatMessage>

        {

            new() { Role = "system", Content = systemPrompt },

            new() { Role = "user", Content = content }

        };

        if (!string.IsNullOrEmpty(selectedFolder))

        {

            messages.Insert(1, new ExtendedChatMessage

            {

                Role = "system",

                Content = $"[Aktif Proje Dizini]\nYol: {selectedFolder}"

            });

        }

        var response = await _apiClient.SendChatWithToolsAsync(messages, new List<ToolDefinition>(), cancellationToken);

        var assistantMessage = response?.Choices?.FirstOrDefault()?.Message?.Content;

        var extractedCode = ExtractCodeFromAiResponse(assistantMessage ?? content);

        return !string.IsNullOrEmpty(extractedCode) ? extractedCode : (assistantMessage ?? content);

    }

    private ChatSession CreateNewSessionInternal(string name)

    {

        var session = new ChatSession { Name = name };

        ChatSessions.Add(session);

        ActiveSession = session;

        return session;

    }

    private static bool OverlapsDiscoveredSnippet(
        RagService.SearchResult ragResult,
        IReadOnlyDictionary<string, List<CodeSnippet>> discoveredSnippets)
    {
        try
        {
            var ragPath = Path.GetFullPath(ragResult.FilePath);
            return discoveredSnippets.Any(entry =>
                Path.GetFullPath(entry.Key).Equals(ragPath, StringComparison.OrdinalIgnoreCase) &&
                entry.Value.Any(snippet =>
                    ragResult.StartLine <= snippet.EndLine && ragResult.EndLine >= snippet.StartLine));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>

    /// Kodda değişiklik yapan tool'ları belirler

    /// </summary>

    private bool IsCodeModificationTool(string toolName)

    {

        var codeModificationTools = new[]

        {

            "CreateOrUpdateFile",

            "CreateFile",

            "DeleteFile",

            "ModifyFile",

            "ApplyPatch",

            "CreateFolder",

            "RenameFile"

        };

        return codeModificationTools.Contains(toolName, StringComparer.OrdinalIgnoreCase);

    }

    /// <summary>

    /// Tool output'ından değişen dosyaların listesini extract eder

    /// </summary>

    private List<string> ExtractChangedFilesFromToolOutput(string output, string toolName)

    {

        var changedFiles = new List<string>();

        if (string.IsNullOrEmpty(output))

            return changedFiles;

        // Satırları kontrol et ve dosya yolları ara

        var lines = output.Split('\n');

        foreach (var line in lines)

        {

            var trimmed = line.Trim();

            // "SAVED_PATH:/path/to/file" formatını kontrol et (CreateOrUpdateFile'dan)

            if (trimmed.StartsWith("SAVED_PATH:"))

            {

                var filePath = trimmed.Substring("SAVED_PATH:".Length).Trim();

                if (!string.IsNullOrEmpty(filePath) && filePath.Length < 500)

                {

                    changedFiles.Add(filePath);

                    continue;

                }

            }

            // "✅ Created: BasucuIDE/MyFile.cs" gibi formatları ara

            if (trimmed.StartsWith("✅ Created:") || 

                trimmed.StartsWith("✅ Modified:") || 

                trimmed.StartsWith("✅ Updated:") ||

                trimmed.StartsWith("✅ Deleted:") ||

                trimmed.StartsWith("📝 Path:") ||

                trimmed.StartsWith("File created:") ||

                trimmed.StartsWith("File modified:") ||

                trimmed.StartsWith("File deleted:"))

            {

                // Dosya yolunu extract et

                var parts = trimmed.Split(new[] { ":", "→" }, StringSplitOptions.None);

                if (parts.Length > 1)

                {

                    var filePath = parts[1].Trim();

                    // Boş yolları ve büyük çıktıları filtrele

                    if (!string.IsNullOrEmpty(filePath) && !filePath.Contains("[") && filePath.Length < 500)

                    {

                        changedFiles.Add(filePath);

                    }

                }

            }

        }

        // Eğer tool output'unda dosya yolu varsa (arg olarak), bunu ekle

        if (changedFiles.Count == 0 && !string.IsNullOrEmpty(output))

        {

            // Varsayılan: output'taki ilk satır dosya yolu olabilir

            var firstLine = output.Split('\n')[0].Trim();

            if (!firstLine.Contains("✅") && !firstLine.Contains("❌") && firstLine.Length < 500)

            {

                changedFiles.Add(firstLine);

            }

        }

        _terminalLog?.Invoke(LocalizationManager.Instance.GetString("VerificationIcinDegisenDosyalar").Replace("{files}", string.Join(", ", changedFiles)));

        return changedFiles;

    }

    private static void UpdateSystemPromptForSelectedTools(
        List<ExtendedChatMessage> messagesToSend,
        List<ToolDefinition> tools,
        string userSystemPrompt,
        bool needsPlan)
    {
        var selectedToolNames = tools.Select(t => t.Function!.Name!);
        var dynamicCatalog = ToolRegistry.GetToolCatalogPrompt(selectedToolNames);
        var mode = SettingsWindow.GetSettings().ActiveWorkspaceMode;
        string dynamicCore = ToolRegistry.GetCoreSystemPrompt(mode) + "\n\n" + dynamicCatalog;
        string dynamicSysPrompt;

        if (!string.IsNullOrEmpty(userSystemPrompt))
        {
            dynamicSysPrompt = dynamicCore
                + "\n\n---\n[KULLANICI KİŞİSELLEŞTİRMESİ - Bu talimatlara uy, ancak araç kullanımı ve güvenlik kurallarını asla atlatma]\n"
                + userSystemPrompt;
        }
        else
        {
            dynamicSysPrompt = dynamicCore;
        }

        if (needsPlan)
        {
            dynamicSysPrompt += "\n\n<planning_instruction>\nCRITICAL INSTRUCTION: Keep your internal thinking (<think>...</think>) concise (under 150 words). Do NOT write full code inside thinking blocks. Immediately invoke tools (WriteFile, ReadFile, etc.) to perform actions.\n</planning_instruction>";
        }

        dynamicSysPrompt += "\n\n<tool_escalation_instruction>\nIf during execution you discover you need a tool that is not in your active tool list (e.g. ExecuteTerminalCommand, SearchCode), output: [REQUEST_TOOL: ToolName]\n</tool_escalation_instruction>";

        dynamicSysPrompt += "\n\n" + ResponseLanguageInstruction;

        var sysMsg = messagesToSend.FirstOrDefault(m => m.Role == "system");
        if (sysMsg != null) sysMsg.Content = dynamicSysPrompt;
    }

    private async Task<string?> DownloadAndSaveImageAsync(string imageUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = new System.Net.Http.HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(60);
            var bytes = await httpClient.GetByteArrayAsync(imageUrl, cancellationToken);

            string targetDir = !string.IsNullOrWhiteSpace(_toolExecutor?.ProjectFolder)
                ? System.IO.Path.Combine(_toolExecutor.ProjectFolder, "generated_images")
                : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Yengi_Images");

            if (!System.IO.Directory.Exists(targetDir))
            {
                System.IO.Directory.CreateDirectory(targetDir);
            }

            string fileName = $"gorsel_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
            string filePath = System.IO.Path.Combine(targetDir, fileName);

            await System.IO.File.WriteAllBytesAsync(filePath, bytes, cancellationToken);
            _terminalLog?.Invoke($"[🎨 Image Studio] Görsel diske kaydedildi: {filePath}");
            return filePath;
        }
        catch (Exception ex)
        {
            _terminalLog?.Invoke($"[🎨 Image Studio] Görsel indirme/kaydetme hatası: {ex.Message}");
            return null;
        }
    }

    private async Task<string> EnhanceImagePromptWithLlmAsync(string rawPrompt, CancellationToken cancellationToken)
    {
        if (_apiClient == null) return rawPrompt;
        try
        {
            _terminalLog?.Invoke($"[🎨 Image Studio] Prompt LLM ile zenginleştiriliyor...");
            var messages = new List<ExtendedChatMessage>
            {
                new ExtendedChatMessage
                {
                    Role = "system",
                    Content = "You are an expert prompt engineer for AI image generators (DALL-E 3, Midjourney, Flux). Take the user's raw prompt and expand it into a detailed, vivid, high-quality image generation prompt in English. Include details about lighting, style, composition, texture, and artistic mood. Output ONLY the final enhanced prompt text in English. Do NOT include any explanations, intro text, quotes, or markdown code blocks."
                },
                new ExtendedChatMessage { Role = "user", Content = rawPrompt }
            };

            var response = await _apiClient.SendChatWithToolsAsync(messages, new List<ToolDefinition>(), cancellationToken);
            string enhanced = response?.Choices?.FirstOrDefault()?.Message?.Content?.Trim() ?? rawPrompt;
            if (!string.IsNullOrWhiteSpace(enhanced))
            {
                _terminalLog?.Invoke($"[🎨 Image Studio] Zenginleştirilmiş Prompt: {enhanced}");
                return enhanced;
            }
        }
        catch (Exception ex)
        {
            _terminalLog?.Invoke($"[🎨 Image Studio] Prompt zenginleştirme uyarısı: {ex.Message}");
        }
        return rawPrompt;
    }

    // ─────────────────────────────────────────────────────────────────────
    // 🎨 GÖRSEL STÜDYOSU — Doğrudan Görsel API Akışı (LLM bypass)
    // ─────────────────────────────────────────────────────────────────────
    private async Task<ChatFlowResult> HandleImageStudioRequestAsync(
        string prompt,
        ChatSession session,
        Action<string>? onTokenReceived,
        Action<ChatFlowMessage>? onMessageAdded,
        CancellationToken cancellationToken)
    {
        var settings = SettingsWindow.GetSettings();

        // Kullanıcı mesajını oturum geçmişine ekle
        session.History.Add(new ExtendedChatMessage { Role = "user", Content = prompt });

        // Prompt zenginleştirme aktifse LLM çağır
        string finalPrompt = prompt;
        if (settings.ImageStudioEnhancePrompt)
        {
            finalPrompt = await EnhanceImagePromptWithLlmAsync(prompt, cancellationToken);
        }

        // API key yoksa veya Ücretsiz Servis seçiliyse → Pollinations.ai
        bool usePollinations = settings.ImageStudioUseFreePollinations || string.IsNullOrWhiteSpace(settings.ImageStudioApiKey);
        if (usePollinations)
        {
            _terminalLog?.Invoke($"[🎨 Image Studio] Pollinations.ai (ücretsiz) kullanılıyor.");
            return await HandlePollinationsImageAsync(finalPrompt, session, onMessageAdded, cancellationToken);
        }

        _terminalLog?.Invoke($"[🎨 Image Studio] Doğrudan API çağrısı başlatıldı. Prompt: {finalPrompt}");

        try
        {
            using var httpClient = new System.Net.Http.HttpClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {settings.ImageStudioApiKey}");

            var requestBody = new
            {
                model = settings.ImageStudioModel,
                prompt = finalPrompt,
                n = 1,
                size = "1024x1024",
                response_format = "url"
            };

            var json = System.Text.Json.JsonSerializer.Serialize(requestBody);
            var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var baseUrl = settings.ImageStudioBaseUrl.TrimEnd('/');
            var response = await httpClient.PostAsync($"{baseUrl}/images/generations", content, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _terminalLog?.Invoke($"[🎨 Image Studio] API Hatası: {response.StatusCode}");
                var errMsg = new ChatFlowMessage
                {
                    Sender = "AI Asistan",
                    Content = $"❌ Görsel üretilemedi. API Hatası ({(int)response.StatusCode}): {responseBody}"
                };
                onMessageAdded?.Invoke(errMsg);
                return new ChatFlowResult
                {
                    Success = false,
                    ErrorMessage = responseBody,
                    Messages = new List<ChatFlowMessage> { errMsg }
                };
            }

            using var doc = System.Text.Json.JsonDocument.Parse(responseBody);
            var imageUrl = doc.RootElement
                .GetProperty("data")[0]
                .GetProperty("url")
                .GetString();

            _terminalLog?.Invoke($"[🎨 Image Studio] Görsel başarıyla üretildi. İndiriliyor...");

            var savedPath = await DownloadAndSaveImageAsync(imageUrl ?? "", cancellationToken);

            string resultText;
            if (!string.IsNullOrEmpty(savedPath))
            {
                var fileUri = new Uri(savedPath).AbsoluteUri;
                resultText = $"✅ Görsel üretildi ve kaydedildi!\n\n![Üretilen Görsel]({fileUri})\n\n📁 **Kaydedilen Yer:** `{savedPath}`";
            }
            else
            {
                resultText = $"✅ Görsel üretildi!\n\n![Üretilen Görsel]({imageUrl})\n\n[Görseli İndir]({imageUrl})";
            }

            var assistantMsg = new ChatFlowMessage { Sender = "AI Asistan", Content = resultText };
            session.History.Add(new ExtendedChatMessage { Role = "assistant", Content = resultText });
            onMessageAdded?.Invoke(assistantMsg);

            return new ChatFlowResult
            {
                Success = true,
                Messages = new List<ChatFlowMessage> { assistantMsg }
            };
        }
        catch (OperationCanceledException)
        {
            return new ChatFlowResult { Success = false, ErrorMessage = "İptal edildi.", Messages = new List<ChatFlowMessage>() };
        }
        catch (Exception ex)
        {
            _terminalLog?.Invoke($"[🎨 Image Studio] Beklenmedik hata: {ex.Message}");
            var errMsg = new ChatFlowMessage { Sender = "AI Asistan", Content = $"❌ Beklenmedik hata: {ex.Message}" };
            onMessageAdded?.Invoke(errMsg);
            return new ChatFlowResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Messages = new List<ChatFlowMessage> { errMsg }
            };
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // 🆓 POLLINATIONS.AI — Ücretsiz, key gerektirmeyen görsel üretimi
    // ─────────────────────────────────────────────────────────────────────
    private async Task<ChatFlowResult> HandlePollinationsImageAsync(
        string prompt,
        ChatSession session,
        Action<ChatFlowMessage>? onMessageAdded,
        CancellationToken cancellationToken)
    {
        try
        {
            var settings = SettingsWindow.GetSettings();
            var baseUrl = !string.IsNullOrWhiteSpace(settings.ImageStudioPublicBaseUrl)
                ? settings.ImageStudioPublicBaseUrl.TrimEnd('/') + "/"
                : "https://image.pollinations.ai/prompt/";

            // URL encode prompt
            var encodedPrompt = Uri.EscapeDataString(prompt);
            var imageUrl = baseUrl.Contains("?")
                ? $"{baseUrl}{encodedPrompt}"
                : $"{baseUrl}{encodedPrompt}?width=1024&height=1024&nologo=true&enhance=true";

            _terminalLog?.Invoke($"[🎨 Image Studio] Topluluk sunucusundan ({baseUrl}) görsel üretiliyor ve indiriliyor...");

            var savedPath = await DownloadAndSaveImageAsync(imageUrl, cancellationToken);

            string resultText;
            if (!string.IsNullOrEmpty(savedPath))
            {
                var fileUri = new Uri(savedPath).AbsoluteUri;
                resultText = $"✅ Görsel üretildi ve projenize kaydedildi! *(Pollinations.ai)*\n\n![Üretilen Görsel]({fileUri})\n\n📁 **Kaydedilen Yer:** `{savedPath}`\n\n> 💡 Kendi API anahtarınızı eklemek için ⚙️ → Görsel Stüdyosu Ayarları.";
            }
            else
            {
                resultText = $"✅ Görsel üretildi! *(Pollinations.ai — ücretsiz)*\n\n![Üretilen Görsel]({imageUrl})\n\n[Görseli İndir]({imageUrl})\n\n> 💡 Kendi API anahtarınızı eklemek için ⚙️ → Görsel Stüdyosu Ayarları.";
            }

            var assistantMsg = new ChatFlowMessage { Sender = "AI Asistan", Content = resultText };
            session.History.Add(new ExtendedChatMessage { Role = "assistant", Content = resultText });
            onMessageAdded?.Invoke(assistantMsg);

            return new ChatFlowResult { Success = true, Messages = new List<ChatFlowMessage> { assistantMsg } };
        }
        catch (OperationCanceledException)
        {
            return new ChatFlowResult { Success = false, ErrorMessage = "İptal edildi.", Messages = new List<ChatFlowMessage>() };
        }
        catch (Exception ex)
        {
            _terminalLog?.Invoke($"[🎨 Pollinations] Hata: {ex.Message}");
            var errMsg = new ChatFlowMessage { Sender = "AI Asistan", Content = $"❌ Pollinations.ai hatası: {ex.Message}" };
            onMessageAdded?.Invoke(errMsg);
            return new ChatFlowResult { Success = false, ErrorMessage = ex.Message, Messages = new List<ChatFlowMessage> { errMsg } };
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // 🧊 BLENDER COPILOT — LLM → bpy script üret → Blender'a gönder
    // ─────────────────────────────────────────────────────────────────────
    private async Task<ChatFlowResult> HandleBlenderRequestAsync(
        string userRequest,
        ChatSession session,
        Action<string>? onTokenReceived,
        Action<ChatFlowMessage>? onMessageAdded,
        CancellationToken cancellationToken)
    {
        var settings = SettingsWindow.GetSettings();

        // Kullanıcı mesajını oturum geçmişine ekle
        session.History.Add(new ExtendedChatMessage { Role = "user", Content = userRequest });

        _terminalLog?.Invoke(LocalizationManager.Instance.GetString("BlenderGorevAlindi").Replace("{req}", userRequest));

        // 1. ADIM: LLM'den bpy scripti üret
        var blenderSystemPrompt = ToolRegistry.GetCoreSystemPrompt(AgentWorkspaceMode.BlenderCopilot);

        var blenderMessages = new List<ExtendedChatMessage>
        {
            new ExtendedChatMessage
            {
                Role = "system",
                Content = blenderSystemPrompt + "\n\nÖNEMLİ: SADECE çalıştırılabilir Python (bpy) kodu döndür. Açıklama ekleme, sadece kod bloğu yaz. Yanıtını ```python ile başlat ve ``` ile bitir. Eğer sohbet geçmişinde daha önce oluşturduğun objeler varsa, yeni komutta o objeleri düzenle, seç, rengini değiştir veya üzerlerine yeni objeler ekle."
            }
        };

        // Geçmiş sohbet mesajlarını ekle (böylece önceki objeleri ve isimlerini hatırlar)
        blenderMessages.AddRange(session.History);

        string generatedScript = "";
        try
        {
            _terminalLog?.Invoke(LocalizationManager.Instance.GetString("BlenderScriptUretiliyor"));

            var response = await _apiClient!.SendChatWithToolsAsync(blenderMessages, new List<ToolDefinition>(), cancellationToken);
            generatedScript = response?.Choices?.FirstOrDefault()?.Message?.Content ?? "";
            onTokenReceived?.Invoke(generatedScript);

            // Markdown kod bloğu varsa temizle (```python veya sadece ```)
            var match = System.Text.RegularExpressions.Regex.Match(
                generatedScript, @"```(?:python)?\s*([\s\S]*?)```", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
            {
                generatedScript = match.Groups[1].Value.Trim();
            }

            // BOM (\uFEFF) ve \r karakterlerini temizle
            generatedScript = generatedScript.Replace("\uFEFF", "").Replace("\r\n", "\n").Replace("\r", "\n").Trim();

            // "import bpy" veya "from bpy" başlangıcını bul ve öncesindeki tüm numaralandırmaları veya açıklamaları kes
            int bpyIdx = generatedScript.IndexOf("import bpy", StringComparison.OrdinalIgnoreCase);
            if (bpyIdx < 0) bpyIdx = generatedScript.IndexOf("from bpy", StringComparison.OrdinalIgnoreCase);
            if (bpyIdx >= 0)
            {
                generatedScript = generatedScript.Substring(bpyIdx).Trim();
            }

            // Satır başlarındaki numaralandırmaları (örn: "1. import bpy", "2. trunk = ...") temizle
            var scriptLines = generatedScript.Split('\n');
            for (int i = 0; i < scriptLines.Length; i++)
            {
                scriptLines[i] = System.Text.RegularExpressions.Regex.Replace(scriptLines[i], @"^\s*\d+[\.\:]?\s+", "");
            }
            generatedScript = string.Join("\n", scriptLines).Trim();
        }
        catch (Exception ex)
        {
            _terminalLog?.Invoke(LocalizationManager.Instance.GetString("BlenderScriptHata").Replace("{msg}", ex.Message));
            var errMsg = new ChatFlowMessage { Sender = "AI Asistan", Content = $"❌ Blender script: {ex.Message}" };
            onMessageAdded?.Invoke(errMsg);
            return new ChatFlowResult { Success = false, ErrorMessage = ex.Message, Messages = new List<ChatFlowMessage> { errMsg } };
        }

        _terminalLog?.Invoke(LocalizationManager.Instance.GetString("BlenderScriptUretildi").Replace("{len}", generatedScript.Length.ToString()));

        // 2. ADIM: Blender WebSocket'e gönder
        string blenderResult;
        try
        {
            blenderResult = await SendScriptToBlenderAsync(generatedScript, settings.BlenderWebSocketPort, cancellationToken);
        }
        catch (Exception ex)
        {
            _terminalLog?.Invoke(LocalizationManager.Instance.GetString("BlenderWebsocketHata").Replace("{msg}", ex.Message));

            // Blender bağlı olmasa bile scripti göster
            var offlineMsg = new ChatFlowMessage
            {
                Sender = "AI Asistan",
                Content = $"⚠️ Blender'a bağlanılamadı (port {settings.BlenderWebSocketPort}). Eklentinin çalıştığından emin olun.\n\nYine de oluşturulan script:\n\n```python\n{generatedScript}\n```\n\nBu scripti Blender'da **Scripting** sekmesine yapıştırıp çalıştırabilirsiniz."
            };
            onMessageAdded?.Invoke(offlineMsg);
            return new ChatFlowResult { Success = false, ErrorMessage = ex.Message, Messages = new List<ChatFlowMessage> { offlineMsg } };
        }

        // 3. ADIM: Sonucu kullanıcıya göster
        session.History.Add(new ExtendedChatMessage { Role = "assistant", Content = $"```python\n{generatedScript}\n```\n\n**Blender Çıktısı:** {blenderResult}" });
        var resultMsg = new ChatFlowMessage
        {
            Sender = "AI Asistan",
            Content = $"✅ Blender scripti çalıştırıldı!\n\n```python\n{generatedScript}\n```\n\n**Blender Yanıtı:** {blenderResult}"
        };
        onMessageAdded?.Invoke(resultMsg);

        return new ChatFlowResult { Success = true, Messages = new List<ChatFlowMessage> { resultMsg } };
    }

    private async Task<string> SendScriptToBlenderAsync(string script, int port, CancellationToken cancellationToken)
    {
        using var client = new System.Net.Sockets.TcpClient();
        await client.ConnectAsync("127.0.0.1", port, cancellationToken);

        var settings = SettingsWindow.GetSettings();
        string payload = $"YENGI_TOKEN:{settings.BlenderSecretToken}\n{script}";

        using var stream = client.GetStream();
        var scriptBytes = System.Text.Encoding.UTF8.GetBytes(payload);
        await stream.WriteAsync(scriptBytes, 0, scriptBytes.Length, cancellationToken);
        try { client.Client.Shutdown(System.Net.Sockets.SocketShutdown.Send); } catch { }

        var buffer = new byte[8192];
        int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
        return System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
    }

    private async Task<ChatFlowResult> HandleUnityRequestAsync(
        string userRequest,
        ChatSession session,
        Action<string>? onTokenReceived,
        Action<ChatFlowMessage>? onMessageAdded,
        CancellationToken cancellationToken)
    {
        var settings = SettingsWindow.GetSettings();

        // Kullanıcı mesajını oturum geçmişine ekle
        session.History.Add(new ExtendedChatMessage { Role = "user", Content = userRequest });

        _terminalLog?.Invoke(LocalizationManager.Instance.GetString("UnityGorevAlindi").Replace("{req}", userRequest));

        // 1. ADIM: LLM'den Unity C# Editor scripti üret
        var unitySystemPrompt = ToolRegistry.GetCoreSystemPrompt(AgentWorkspaceMode.UnityCopilot);

        var unityMessages = new List<ExtendedChatMessage>
        {
            new ExtendedChatMessage
            {
                Role = "system",
                Content = unitySystemPrompt + "\n\nÖNEMLİ: SADECE çalıştırılabilir C# (Unity Editor / UnityEngine) kodu döndür. Açıklama ekleme, sadece kod bloğu yaz. Yanıtını ```csharp veya ```cs ile başlat ve ``` ile bitir. Eğer sohbet geçmişinde daha önce oluşturduğun objeler veya Editor scriptleri varsa, yeni komutta onları düzenle veya üzerlerine yeni özellikler ekle."
            }
        };

        unityMessages.AddRange(session.History);

        string generatedScript = "";
        try
        {
            _terminalLog?.Invoke(LocalizationManager.Instance.GetString("UnityScriptUretiliyor"));

            var response = await _apiClient!.SendChatWithToolsAsync(unityMessages, new List<ToolDefinition>(), cancellationToken);
            generatedScript = response?.Choices?.FirstOrDefault()?.Message?.Content ?? "";
            onTokenReceived?.Invoke(generatedScript);

            var match = System.Text.RegularExpressions.Regex.Match(
                generatedScript, @"```(?:csharp|cs)\s*([\s\S]*?)```", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
                generatedScript = match.Groups[1].Value.Trim();

            // BOM (\uFEFF) ve \r karakterlerini temizle
            generatedScript = generatedScript.Replace("\uFEFF", "").Replace("\r\n", "\n").Replace("\r", "\n").Trim();

            // "using ", "namespace ", "public class " öncesindeki numaralandırmaları veya açıklamaları kes
            int codeIdx = generatedScript.IndexOf("using ", StringComparison.OrdinalIgnoreCase);
            if (codeIdx < 0) codeIdx = generatedScript.IndexOf("namespace ", StringComparison.OrdinalIgnoreCase);
            if (codeIdx < 0) codeIdx = generatedScript.IndexOf("public class ", StringComparison.OrdinalIgnoreCase);
            if (codeIdx >= 0)
            {
                generatedScript = generatedScript.Substring(codeIdx).Trim();
            }

            // Satır başlarındaki numaralandırmaları (örn: "1. using UnityEngine;") temizle
            var scriptLines = generatedScript.Split('\n');
            for (int i = 0; i < scriptLines.Length; i++)
            {
                scriptLines[i] = System.Text.RegularExpressions.Regex.Replace(scriptLines[i], @"^\s*\d+[\.\:]?\s+", "");
            }
            generatedScript = string.Join("\n", scriptLines).Trim();

            // Eksik namespace'leri otomatik tamir et
            if (generatedScript.Contains("EditorSceneManager") && !generatedScript.Contains("UnityEditor.SceneManagement"))
            {
                generatedScript = "using UnityEditor.SceneManagement;\n" + generatedScript;
            }
            if ((generatedScript.Contains("Editor") || generatedScript.Contains("MenuItem") || generatedScript.Contains("InitializeOnLoad")) && !generatedScript.Contains("using UnityEditor;"))
            {
                generatedScript = "using UnityEditor;\n" + generatedScript;
            }
            if (!generatedScript.Contains("using UnityEngine;"))
            {
                generatedScript = "using UnityEngine;\n" + generatedScript;
            }
        }
        catch (Exception ex)
        {
            _terminalLog?.Invoke(LocalizationManager.Instance.GetString("UnityScriptHata").Replace("{msg}", ex.Message));
            var errMsg = new ChatFlowMessage { Sender = "AI Asistan", Content = $"❌ Unity script: {ex.Message}" };
            onMessageAdded?.Invoke(errMsg);
            return new ChatFlowResult { Success = false, ErrorMessage = ex.Message, Messages = new List<ChatFlowMessage> { errMsg } };
        }

        _terminalLog?.Invoke(LocalizationManager.Instance.GetString("UnityScriptUretildi").Replace("{len}", generatedScript.Length.ToString()));

        // 2. ADIM: Unity Socket'e gönder
        string unityResult;
        try
        {
            unityResult = await SendScriptToUnityAsync(generatedScript, settings.UnityWebSocketPort, cancellationToken);
        }
        catch (Exception ex)
        {
            _terminalLog?.Invoke(LocalizationManager.Instance.GetString("UnitySocketHata").Replace("{msg}", ex.Message));

            var offlineMsg = new ChatFlowMessage
            {
                Sender = "AI Asistan",
                Content = $"⚠️ Unity Editor'e bağlanılamadı (port {settings.UnityWebSocketPort}). Yengi Unity eklenti scriptinin (`YengiUnityCopilot.cs`) projenizde Editor klasöründe çalıştığından emin olun.\n\nYine de oluşturulan C# scripti:\n\n```csharp\n{generatedScript}\n```"
            };
            onMessageAdded?.Invoke(offlineMsg);
            return new ChatFlowResult { Success = false, ErrorMessage = ex.Message, Messages = new List<ChatFlowMessage> { offlineMsg } };
        }

        // 3. ADIM: Sonucu kullanıcıya göster
        session.History.Add(new ExtendedChatMessage { Role = "assistant", Content = $"```csharp\n{generatedScript}\n```\n\n**Unity Çıktısı:** {unityResult}" });
        var resultMsg = new ChatFlowMessage
        {
            Sender = "AI Asistan",
            Content = $"✅ Unity C# scripti çalıştırıldı!\n\n```csharp\n{generatedScript}\n```\n\n**Unity Yanıtı:** {unityResult}"
        };
        onMessageAdded?.Invoke(resultMsg);

        return new ChatFlowResult { Success = true, Messages = new List<ChatFlowMessage> { resultMsg } };
    }

    private async Task<string> SendScriptToUnityAsync(string script, int port, CancellationToken cancellationToken)
    {
        using var client = new System.Net.Sockets.TcpClient();
        await client.ConnectAsync("127.0.0.1", port, cancellationToken);

        using var stream = client.GetStream();
        var scriptBytes = System.Text.Encoding.UTF8.GetBytes(script);
        await stream.WriteAsync(scriptBytes, 0, scriptBytes.Length, cancellationToken);
        try { client.Client.Shutdown(System.Net.Sockets.SocketShutdown.Send); } catch { }

        var buffer = new byte[8192];
        int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
        return System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
    }
}
