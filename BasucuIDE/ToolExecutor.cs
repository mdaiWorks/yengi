using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using mdaiAgent.Services;

namespace mdaiAgent;

public class ToolResult
{
    public bool Success { get; set; }
    public string Output { get; set; } = "";
    public string? Error { get; set; }
}

public delegate Task<T> UiInvoker<T>(Func<Task<T>> operation);

public class ToolExecutor
{
    public enum ConfirmResult
    {
        Allow,
        Skip,
        DryRun,
        Cancel
    }
    private readonly string? _projectFolder;
    private readonly Action<string> _terminalLog;
    private readonly Func<string, Task<ConfirmResult>> _confirmCommand;
    private readonly Func<string, string, string, Task<bool>> _confirmFileChange;
    private readonly Func<string, List<string>, Task<string>>? _askUserOptions;
    private readonly Func<string, List<string>, bool, Task<string>>? _askUserOptionsWithSelection;
    private readonly UiInvoker<ConfirmResult>? _confirmUiInvoker;
    private readonly UiInvoker<bool>? _fileChangeUiInvoker;
    private readonly bool _safeAutomationEnabled;
    private readonly Action<string>? _updateOperationStep;
    private readonly Action<string, NotificationSeverity>? _notify;
    private readonly ContextOptimizerService _optimizer = new ContextOptimizerService();
    private readonly AppSettings? _settings;
    private readonly SubAgentResultCoordinator _subAgentCoordinator;
    private readonly ExternalPathAccessManager _externalPathAccess;
    private readonly ToolOutputStore _toolOutputStore;

    // Services for separated concerns
    private FileOperationsService? _fileService;
    private SearchService? _searchService;
    private BuildService? _buildService;
    private CheckpointService? _checkpointService;
    private PlanningService? _planningService;
    private DelegationService? _delegationService;
    private WebService? _webService;
    private CommandService? _commandService;
    private UtilityService? _utilityService;

    public string? ProjectFolder => _projectFolder;
    public SubAgentResultCoordinator SubAgentCoordinator => _subAgentCoordinator;

    public Task<ToolResult> RequestUserOptionsAsync(string question, List<string> options, bool allowMultiple = false)
    {
        var arguments = JsonSerializer.Serialize(new
        {
            question,
            options,
            allowMultiple
        });
        return _utilityService!.AskUserOptionsAsync(JsonDocument.Parse(arguments).RootElement);
    }

    public ToolExecutor(string? projectFolder, Action<string> terminalLog, Func<string, Task<ConfirmResult>> confirmCommand, Func<string, string, string, Task<bool>> confirmFileChange, bool safeAutomationEnabled = false, Action<string>? updateOperationStep = null, Action<string, NotificationSeverity>? notify = null, UiInvoker<ConfirmResult>? confirmUiInvoker = null, UiInvoker<bool>? fileChangeUiInvoker = null, AppSettings? settings = null, Func<string, List<string>, Task<string>>? askUserOptions = null, Func<string, List<string>, bool, Task<string>>? askUserOptionsWithSelection = null, Func<string, Task<bool>>? requestExternalFolderAccess = null)
    {
        _projectFolder = projectFolder;
        _terminalLog = terminalLog;
        _confirmCommand = confirmCommand;
        _confirmFileChange = confirmFileChange;
        _askUserOptions = askUserOptions;
        _askUserOptionsWithSelection = askUserOptionsWithSelection;
        _confirmUiInvoker = confirmUiInvoker;
        _fileChangeUiInvoker = fileChangeUiInvoker;
        _safeAutomationEnabled = safeAutomationEnabled;
        _updateOperationStep = updateOperationStep;
        _notify = notify;
        _settings = settings;
        _subAgentCoordinator = new SubAgentResultCoordinator(terminalLog);
        _externalPathAccess = new ExternalPathAccessManager(projectFolder, requestExternalFolderAccess);
        _toolOutputStore = new ToolOutputStore();

        // Initialize AI plan generator from AppSettings (provider abstraction)
        AiPlanGenerator? aiPlanGenerator = null;
        if (settings != null)
        {
            try
            {
                var apiClient = AiProviderFactory.CreateProvider(settings);
                aiPlanGenerator = new AiPlanGenerator(apiClient, terminalLog);
            }
            catch (Exception ex)
            {
                terminalLog($"⚠️ AiPlanGenerator başlatma hatası: {ex.Message}");
            }
        }

        // Initialize services
        _fileService = new FileOperationsService(projectFolder, terminalLog, confirmFileChange, fileChangeUiInvoker, _externalPathAccess);
        _searchService = new SearchService(projectFolder, terminalLog, _externalPathAccess);
        _buildService = new BuildService(projectFolder, terminalLog, updateOperationStep, _externalPathAccess, _toolOutputStore);
        _checkpointService = new CheckpointService(projectFolder, terminalLog);
        _planningService = new PlanningService(projectFolder, terminalLog, _subAgentCoordinator, aiPlanGenerator);
        _delegationService = new DelegationService(projectFolder, terminalLog, _subAgentCoordinator);
        _webService = new WebService(projectFolder, terminalLog);
        _commandService = new CommandService(projectFolder, terminalLog);
        _utilityService = _askUserOptionsWithSelection != null
            ? new UtilityService(projectFolder, terminalLog, _askUserOptionsWithSelection, updateOperationStep)
            : new UtilityService(projectFolder, terminalLog, askUserOptions, updateOperationStep);
    }

    public ToolExecutor(string? projectFolder, Action<string> terminalLog, Func<string, Task<bool>> confirmCommand, Func<string, string, string, Task<bool>> confirmFileChange, bool safeAutomationEnabled = false, Action<string>? updateOperationStep = null, Action<string, NotificationSeverity>? notify = null, UiInvoker<ConfirmResult>? uiInvoker = null, UiInvoker<bool>? fileChangeUiInvoker = null, Func<string, List<string>, Task<string>>? askUserOptions = null, Func<string, List<string>, bool, Task<string>>? askUserOptionsWithSelection = null, Func<string, Task<bool>>? requestExternalFolderAccess = null)
        : this(projectFolder, terminalLog, async cmd => (await confirmCommand(cmd)) ? ConfirmResult.Allow : ConfirmResult.Cancel, confirmFileChange, safeAutomationEnabled, updateOperationStep, notify, uiInvoker, fileChangeUiInvoker, null, askUserOptions, askUserOptionsWithSelection, requestExternalFolderAccess)
    {
    }

    public async Task<ToolResult> ExecuteAsync(string toolName, string argumentsJson, CancellationToken cancellationToken = default, string? additionalContext = null)
    {
        var toolEvent = new ToolCallEvent
        {
            ToolName = toolName,
            Parameters = argumentsJson
        };

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            _updateOperationStep?.Invoke("🔄 Araç çalıştırılıyor: " + toolName);
            _terminalLog(LocalizationManager.Instance.GetString("AracCagrisiToolName").Replace("{toolName}", toolName));
            Logger.LogInfo($"Tool call: {toolName} {argumentsJson}");

            // JSON geçerliliğini başta kontrol et
            JsonElement arguments;
            try
            {
                arguments = JsonDocument.Parse(argumentsJson ?? "{}").RootElement;
            }
            catch (JsonException jsonEx)
            {
                stopwatch.Stop();
                TokenTrackerService.Instance.LogToolExecution(toolName, false, stopwatch.ElapsedMilliseconds);
                
                var parseError = $"Araç '{toolName}' için geçersiz JSON argümanı: {jsonEx.Message}. Lütfen argümanları düzeltin ve tekrar deneyin.";
                _terminalLog($"Araç hatası (JSON): {parseError}");
                return new ToolResult { Success = false, Error = parseError };
            }

            var externalPath = GetPathArgument(toolName, arguments);
            if (!string.IsNullOrWhiteSpace(externalPath) && !Path.IsPathRooted(externalPath) && !string.IsNullOrWhiteSpace(_projectFolder))
            {
                externalPath = Path.GetFullPath(Path.Combine(_projectFolder, externalPath));
            }

            if (!string.IsNullOrWhiteSpace(externalPath) && !await _externalPathAccess.EnsureAllowedAsync(externalPath))
            {
                return new ToolResult { Success = false, Error = $"Harici klasör erişimi kullanıcı tarafından onaylanmadı: {externalPath}" };
            }

            if (toolName == "CreatePlan" && !string.IsNullOrWhiteSpace(additionalContext))
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(argumentsJson) ?? new Dictionary<string, object>();
                var currentContext = dict.TryGetValue("context", out var ctx) ? ctx.ToString() : "";
                dict["context"] = currentContext + "\n\n" + additionalContext;
                argumentsJson = JsonSerializer.Serialize(dict);
                arguments = JsonDocument.Parse(argumentsJson).RootElement;
            }

            var result = toolName switch
            {
                // File Operations → FileOperationsService
                "ReadFile"           => await _fileService!.ReadFileAsync(arguments, cancellationToken),
                "CreateOrUpdateFile" => await _fileService!.CreateOrUpdateFileAsync(arguments, cancellationToken),
                "ReplaceFileContent" => await _fileService!.ReplaceFileContentAsync(arguments, cancellationToken),
                "ListDirectory"      => await _fileService!.ListDirectoryAsync(arguments, cancellationToken),

                // Search → SearchService
                "FindFiles"  => await _searchService!.FindFilesAsync(arguments, cancellationToken),
                "SearchCode" => await _searchService!.SearchCodeAsync(arguments, cancellationToken),

                // Build & Execute → BuildService (via safety wrappers for confirmation)
                "BuildProject"          => await SafelyBuildProjectAsync(arguments, cancellationToken),
                "RunTests"              => await SafelyRunTestsAsync(arguments, cancellationToken),
                "ExecuteTerminalCommand" => await SafelyExecuteTerminalCommandAsync(arguments, cancellationToken),
                "ReadToolOutput"         => await ReadToolOutputAsync(arguments, cancellationToken),

                // Checkpoint → CheckpointService
                "CreateCheckpoint"    => await _checkpointService!.CreateCheckpointAsync(arguments, cancellationToken),
                "RollbackToCheckpoint" => await _checkpointService!.RollbackToCheckpointAsync(arguments, cancellationToken),

                // Planning → PlanningService
                "CreatePlan"        => await _planningService!.CreatePlanAsync(arguments, cancellationToken),
                "GenerateDiff"      => await _planningService!.GenerateDiffAsync(arguments, cancellationToken),
                "ReadProjectMemory" => await _planningService!.ReadProjectMemoryAsync(arguments, cancellationToken),
                "WriteProjectMemory" => await _planningService!.WriteProjectMemoryAsync(arguments, cancellationToken),
                "SearchProjectMemory" => await _planningService!.SearchProjectMemoryAsync(arguments, cancellationToken),
                "ArchiveProjectMemory" => await _planningService!.ArchiveProjectMemoryAsync(arguments, cancellationToken),
                "RetryPlan"         => await _planningService!.GenerateRecoveryPlanAsync(arguments, cancellationToken),
                "CreateTaskGraph"   => await _planningService!.GenerateTaskGraphAsync(arguments, cancellationToken),

                // Delegation → DelegationService
                "DelegateTask"          => await _delegationService!.DelegateTaskAsync(arguments, cancellationToken),
                "DiscoverProjectContext" => await _delegationService!.DiscoverProjectContextAsync(arguments, cancellationToken),

                // Web → WebService
                "WebSearch"    => await _webService!.WebSearchAsync(arguments, cancellationToken),
                "WebFetch"     => await _webService!.WebFetchAsync(arguments, cancellationToken),
                "TakeScreenshot" => await _webService!.TakeScreenshotAsync(arguments, cancellationToken),

                // Image Studio → Phase 2 Stub
                "GenerateImage" => await SafelyGenerateImageAsync(arguments, cancellationToken),

                // Commands → CommandService
                "CreateQuickCommand"  => await _commandService!.CreateQuickCommandAsync(arguments, cancellationToken),
                "ExecuteQuickCommand" => await _commandService!.ExecuteQuickCommandAsync(arguments, cancellationToken),

                // Utility → UtilityService
                "AskUserOptions" => await _utilityService!.AskUserOptionsAsync(arguments, cancellationToken),
                "SmartRecovery" => await _utilityService!.SmartRecoveryAsync(arguments, cancellationToken),

                // [FAZ 2] Kalıcı Proje Hafızası → PlanningService
                "WriteDecision" => await _planningService!.WriteDecisionAsync(arguments, cancellationToken),
                "WriteTask"     => await _planningService!.WriteTaskAsync(arguments, cancellationToken),

                _ => new ToolResult { Success = false, Error = $"Bilinmeyen araç: '{toolName}'. Geçerli araçları kullanın (büyük/küçük harf ve yazımına dikkat edin)." }

            };

            stopwatch.Stop();
            TokenTrackerService.Instance.LogToolExecution(toolName, result.Success, stopwatch.ElapsedMilliseconds);

            toolEvent.EndTime = DateTime.Now;
            toolEvent.IsSuccess = result.Success;
            toolEvent.Output = result.Output;
            toolEvent.ErrorMessage = result.Error;
            EventBus.Publish(toolEvent);

            return result;
        }
        catch (KeyNotFoundException knfEx)
        {
            stopwatch.Stop();
            TokenTrackerService.Instance.LogToolExecution(toolName, false, stopwatch.ElapsedMilliseconds);

            // Model gerekli bir argümanı eksik gönderdi
            toolEvent.EndTime = DateTime.Now;
            toolEvent.IsSuccess = false;
            toolEvent.ErrorMessage = knfEx.Message;
            EventBus.Publish(toolEvent);

            var msg = $"Araç '{toolName}' için gerekli argüman eksik: {knfEx.Message}. Lütfen gerekli tüm alanları sağlayın.";
            _terminalLog($"Araç hatası (eksik argüman): {msg}");
            return new ToolResult { Success = false, Error = msg };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            TokenTrackerService.Instance.LogToolExecution(toolName, false, stopwatch.ElapsedMilliseconds);

            toolEvent.EndTime = DateTime.Now;
            toolEvent.IsSuccess = false;
            toolEvent.ErrorMessage = ex.Message;
            EventBus.Publish(toolEvent);

            _updateOperationStep?.Invoke("❌ Araç hatası: " + ex.Message);
            _terminalLog($"Araç hatası: {ex.Message}");
            _notify?.Invoke($"Araç hatası: {toolName} - {ex.Message}", NotificationSeverity.Error);
            Logger.LogError($"Tool execute error ({toolName}): {ex}");
            return new ToolResult { Success = false, Error = ex.Message };
        }
    }

    private async Task<ToolResult> ReadToolOutputAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var outputId = arguments.GetProperty("outputId").GetString();
        int? startLine = arguments.TryGetProperty("startLine", out var startProp) && startProp.ValueKind == JsonValueKind.Number
            ? startProp.GetInt32()
            : null;
        int? endLine = arguments.TryGetProperty("endLine", out var endProp) && endProp.ValueKind == JsonValueKind.Number
            ? endProp.GetInt32()
            : null;

        cancellationToken.ThrowIfCancellationRequested();
        return await _toolOutputStore.ReadAsync(outputId ?? "", startLine, endLine);
    }

    private static string? GetPathArgument(string toolName, JsonElement arguments)
    {
        var propertyName = toolName switch
        {
            "ReadFile" or "CreateOrUpdateFile" or "ReplaceFileContent" => "filePath",
            "ListDirectory" => "path",
            "FindFiles" or "SearchCode" => "rootPath",
            "ExecuteTerminalCommand" => "workingDirectory",
            "BuildProject" or "RunTests" => "projectPath",
            _ => null
        };

        return propertyName != null && arguments.TryGetProperty(propertyName, out var property)
            ? property.GetString()
            : null;
    }

    private static bool RequiresCapabilityConfirmation(string toolName)
    {
        return toolName is "ExecuteTerminalCommand" or "BuildProject" or "RunTests";
    }

    private async Task<ToolResult> AskUserOptionsAsync(JsonElement arguments)
    {
        var question = arguments.GetProperty("question").GetString() ?? "Seçiminiz nedir?";
        var options = new List<string>();
        if (arguments.TryGetProperty("options", out var optionsElement))
        {
            foreach (var opt in optionsElement.EnumerateArray())
            {
                var val = opt.GetString();
                if (!string.IsNullOrEmpty(val))
                    options.Add(val);
            }
        }

        if (_askUserOptions != null)
        {
            _updateOperationStep?.Invoke("Kullanıcı seçimi bekleniyor...");
            var result = await _askUserOptions(question, options);
            return new ToolResult { Success = true, Output = result };
        }

        return new ToolResult { Success = false, Error = "AskUserOptions delegesi tanımlanmamış." };
    }

    private async Task<ToolResult> SafelyExecuteTerminalCommandAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var command = arguments.GetProperty("command").GetString()!;
        if (string.IsNullOrWhiteSpace(command))
        {
            return new ToolResult { Success = false, Error = "Komut boş olamaz." };
        }

        var riskAssessment = TerminalCommandRiskAnalyzer.Analyze(command);
        if (riskAssessment.Level == TerminalCommandRiskLevel.Critical)
        {
            var criticalError = $"Komut kritik risk nedeniyle engellendi: {riskAssessment.Reason}";
            _terminalLog($"Komut risk politikası: {criticalError}");
            return new ToolResult { Success = false, Error = criticalError };
        }

        var validationError = ValidateTerminalCommand(command);
        if (!string.IsNullOrEmpty(validationError))
        {
            _terminalLog($"Komut doğrulama hatası: {validationError}");
            return new ToolResult { Success = false, Error = validationError };
        }

        ConfirmResult confirmRes;
        var requiresApproval = _safeAutomationEnabled || riskAssessment.Level >= TerminalCommandRiskLevel.Medium;
        if (requiresApproval)
        {
            var approvalReason = _safeAutomationEnabled
                ? "Güvenli mod etkin."
                : $"Risk seviyesi: {riskAssessment.Level}. {riskAssessment.Reason}";
            confirmRes = await InvokeConfirmAsync($"AI şu komutu çalıştırmak istiyor:\n{command}\n\n{approvalReason}\nOnaylıyor musun?");
            if (confirmRes == ConfirmResult.Skip || confirmRes == ConfirmResult.Cancel)
            {
                return new ToolResult { Success = false, Error = "Komut kullanıcı onayıyla durduruldu." };
            }
            if (confirmRes == ConfirmResult.DryRun)
            {
                return new ToolResult { Success = true, Output = "Dry run: komut simülasyonu tamamlandı." };
            }
        }
        else
        {
            _terminalLog($"Auto Mode: Terminal komutu onaysız çalıştırılıyor: {command}");
        }

        _terminalLog(LocalizationManager.Instance.GetString("KomutCalistiriliyor").Replace("{cmd}", command));
        return await _buildService!.ExecuteTerminalCommandAsync(arguments, cancellationToken);
    }

    private async Task<ToolResult> SafelyGenerateImageAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var settings = SettingsWindow.GetSettings();
        if (!settings.EnableMultiAgentImageGeneration)
        {
            return new ToolResult
            {
                Success = false,
                Output = "Multi-Agent görsel üretimi ayarlardan devre dışı bırakılmış. Görsel Stüdyosu Ayarları (⚙️) menüsünden 'Multi-Agent Modu'nu aktif edin."
            };
        }

        var prompt = arguments.TryGetProperty("prompt", out var pElement) ? pElement.GetString() : "Bilinmeyen Görsel";
        if (string.IsNullOrWhiteSpace(prompt)) prompt = "abstract image";

        _terminalLog?.Invoke($"[🎨 Multi-Agent] Kodlama ajanı görsel üretiyor: {prompt}");

        try
        {
            using var httpClient = new System.Net.Http.HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(60);

            byte[] imageBytes;
            bool usePublic = settings.ImageStudioUseFreePollinations || string.IsNullOrWhiteSpace(settings.ImageStudioApiKey);

            if (usePublic)
            {
                var baseUrl = !string.IsNullOrWhiteSpace(settings.ImageStudioPublicBaseUrl)
                    ? settings.ImageStudioPublicBaseUrl.TrimEnd('/') + "/"
                    : "https://image.pollinations.ai/prompt/";

                var encodedPrompt = Uri.EscapeDataString(prompt);
                var imageUrl = baseUrl.Contains("?")
                    ? $"{baseUrl}{encodedPrompt}"
                    : $"{baseUrl}{encodedPrompt}?width=1024&height=1024&nologo=true&enhance=true";

                imageBytes = await httpClient.GetByteArrayAsync(imageUrl, cancellationToken);
            }
            else
            {
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {settings.ImageStudioApiKey}");
                var requestBody = new
                {
                    model = settings.ImageStudioModel,
                    prompt = prompt,
                    n = 1,
                    size = "1024x1024",
                    response_format = "url"
                };

                var jsonContent = new System.Net.Http.StringContent(
                    System.Text.Json.JsonSerializer.Serialize(requestBody),
                    System.Text.Encoding.UTF8,
                    "application/json");

                var url = $"{settings.ImageStudioBaseUrl.TrimEnd('/')}/images/generations";
                var httpResponse = await httpClient.PostAsync(url, jsonContent, cancellationToken);
                var responseBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

                using var doc = System.Text.Json.JsonDocument.Parse(responseBody);
                var imageUrl = doc.RootElement.GetProperty("data")[0].GetProperty("url").GetString();
                imageBytes = await httpClient.GetByteArrayAsync(imageUrl ?? "", cancellationToken);
            }

            // Dosyayı kaydet
            string targetDir = !string.IsNullOrWhiteSpace(ProjectFolder)
                ? System.IO.Path.Combine(ProjectFolder, "generated_images")
                : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Yengi_Images");

            if (!System.IO.Directory.Exists(targetDir))
            {
                System.IO.Directory.CreateDirectory(targetDir);
            }

            string fileName = $"gorsel_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
            string fullPath = System.IO.Path.Combine(targetDir, fileName);
            await System.IO.File.WriteAllBytesAsync(fullPath, imageBytes, cancellationToken);

            string relativePath = !string.IsNullOrWhiteSpace(ProjectFolder)
                ? System.IO.Path.GetRelativePath(ProjectFolder, fullPath).Replace('\\', '/')
                : fullPath;

            _terminalLog?.Invoke($"[🎨 Multi-Agent] Görsel üretildi ve kaydedildi: {relativePath}");

            return new ToolResult
            {
                Success = true,
                Output = System.Text.Json.JsonSerializer.Serialize(new
                {
                    success = true,
                    prompt = prompt,
                    relative_path = relativePath,
                    full_path = fullPath,
                    html_img_tag = $"<img src=\"{relativePath}\" alt=\"{prompt}\" />",
                    markdown_link = $"![{prompt}]({relativePath})"
                })
            };
        }
        catch (Exception ex)
        {
            _terminalLog?.Invoke($"[🎨 Multi-Agent] Görsel üretme hatası: {ex.Message}");
            return new ToolResult
            {
                Success = false,
                Output = $"Görsel üretilemedi: {ex.Message}"
            };
        }
    }

    private static string ShortenText(string text, int maxLength = 40000)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        if (text.Length <= maxLength)
            return text;

        return text.Substring(0, maxLength) + Environment.NewLine + "\n[Çıktı çok uzun olduğu için kırpıldı]";
    }

    private string? ValidateTerminalCommand(string command)
    {
        var trimmed = command.Trim();
        if (trimmed.Length > 1024)
            return "Komut çok uzun. Lütfen daha kısa bir komut kullanın.";

        var projectContextError = ValidateCommandAgainstProjectType(trimmed);
        if (!string.IsNullOrEmpty(projectContextError))
            return projectContextError;

        return null;
    }

    private string? ValidateCommandAgainstProjectType(string command)
    {
        if (string.IsNullOrEmpty(_projectFolder))
            return null;

        var projectType = ProjectTypeDetector.DetectProjectType(_projectFolder);
        if (projectType == "Flutter" && command.Contains("flutter", StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(Path.Combine(_projectFolder, "pubspec.yaml")))
                return "Bu klasör Flutter projesi gibi görünmüyor. `flutter` komutu yalnızca Flutter projesinde kullanılmalıdır.";
        }

        if (projectType == "Node" && command.Contains("npm", StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(Path.Combine(_projectFolder, "package.json")))
                return "Bu klasör Node.js projesi gibi görünmüyor. `npm` komutu yalnızca Node.js projesinde kullanılmalıdır.";
        }

        if (projectType == "DotNet" && command.Contains("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var hasProj = Directory.EnumerateFiles(_projectFolder, "*.csproj", SearchOption.TopDirectoryOnly).Any();
            var hasSln = Directory.EnumerateFiles(_projectFolder, "*.sln", SearchOption.TopDirectoryOnly).Any();
            if (!hasProj && !hasSln)
                return "Bu klasörde .NET proje dosyası bulunamadı. `dotnet` komutu yalnızca .NET projeleri için kullanılmalıdır.";
        }

        if (command.Contains("python", StringComparison.OrdinalIgnoreCase) || command.Contains("pip", StringComparison.OrdinalIgnoreCase))
        {
            var hasPythonFiles = Directory.EnumerateFiles(_projectFolder, "*.py", SearchOption.AllDirectories).Any();
            var hasPythonConfig = File.Exists(Path.Combine(_projectFolder, "requirements.txt")) || File.Exists(Path.Combine(_projectFolder, "pyproject.toml"));
            if (!hasPythonFiles && !hasPythonConfig)
                return "Bu klasörde Python projesi tespit edilemedi. `python` veya `pip` komutu sadece Python projesinde kullanılmalıdır.";
        }

        if (command.Contains("npm", StringComparison.OrdinalIgnoreCase) || command.Contains("yarn", StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(Path.Combine(_projectFolder, "package.json")))
                return "Bu klasörde package.json bulunamadı. `npm` veya `yarn` komutları sadece Node.js projesinde kullanılmalıdır.";
        }

        return null;
    }

    private async Task<ConfirmResult> InvokeConfirmAsync(string message)
    {
        if (_confirmUiInvoker != null)
        {
            return await _confirmUiInvoker(() => _confirmCommand(message));
        }

        return await _confirmCommand(message);
    }

    private async Task<bool> InvokeFileChangeConfirmAsync(string filePath, string oldContent, string newContent)
    {
        if (_fileChangeUiInvoker != null)
        {
            return await _fileChangeUiInvoker(() => _confirmFileChange(filePath, oldContent, newContent));
        }

        return await _confirmFileChange(filePath, oldContent, newContent);
    }

    private string ResolvePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        var isPlaceholder = path.Contains("KullanıcıProjeKlasörü", StringComparison.OrdinalIgnoreCase) ||
                            path.Contains("UserProjectFolder", StringComparison.OrdinalIgnoreCase);

        if (isPlaceholder && !string.IsNullOrEmpty(_projectFolder))
        {
            var relPart = Path.GetFileName(path);
            return Path.GetFullPath(Path.Combine(_projectFolder, relPart));
        }

        var fullPath = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : !string.IsNullOrEmpty(_projectFolder)
                ? Path.GetFullPath(Path.Combine(_projectFolder, path))
                : Path.GetFullPath(path);

        if (!string.IsNullOrEmpty(_projectFolder))
        {
            var normalizedProjectFolder = Path.GetFullPath(_projectFolder);
            var normalizedFullPath = Path.GetFullPath(fullPath);
            var normalizedRoot = Path.GetFullPath(Path.GetPathRoot(normalizedFullPath) ?? normalizedProjectFolder);

            try
            {
                var hasProjectBoundary = IsWithinProjectBoundary(normalizedFullPath, normalizedProjectFolder);
                if (!hasProjectBoundary)
                {
                    throw new InvalidOperationException($"Proje dışına erişim reddedildi: '{path}'");
                }

                var currentRoot = Path.GetPathRoot(normalizedProjectFolder);
                if (!string.IsNullOrEmpty(currentRoot) && normalizedRoot.Equals(currentRoot, StringComparison.OrdinalIgnoreCase) && normalizedFullPath.StartsWith(normalizedProjectFolder, StringComparison.OrdinalIgnoreCase))
                {
                    return normalizedFullPath;
                }
            }
            catch (Exception)
            {
                Logger.LogInfo($"[PathGuard] Proje dışı yol reddedildi: '{path}'");
                _terminalLog($"⚠️ [PathGuard] Proje dışına erişim reddedildi: {path}");
                throw;
            }
        }

        return fullPath;
    }

    private static bool IsWithinProjectBoundary(string candidatePath, string projectRoot)
    {
        var normalizedCandidate = Path.GetFullPath(candidatePath);
        var normalizedProjectRoot = Path.GetFullPath(projectRoot);

        if (normalizedCandidate.Equals(normalizedProjectRoot, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            var relativePath = Path.GetRelativePath(normalizedProjectRoot, normalizedCandidate);
            if (relativePath.StartsWith("..") || Path.IsPathRooted(relativePath))
                return false;
        }
        catch
        {
            return false;
        }

        // Symlink / NTFS junction kontrolü:
        // Lexical olarak sınır içinde görünse bile, sembolik bağlantının
        // gerçek hedefi proje dışına işaret edebilir.
        try
        {
            var resolvedTarget = ResolveSymlinkTarget(normalizedCandidate);
            if (resolvedTarget != null && !resolvedTarget.Equals(normalizedCandidate, StringComparison.OrdinalIgnoreCase))
            {
                // Gerçek hedef de sınır içinde mi kontrol et
                var resolvedNormalized = Path.GetFullPath(resolvedTarget);
                var relativeOfResolved = Path.GetRelativePath(normalizedProjectRoot, resolvedNormalized);
                if (relativeOfResolved.StartsWith("..") || Path.IsPathRooted(relativeOfResolved))
                    return false; // Sembolik bağlantı proje dışına işaret ediyor
            }
        }
        catch
        {
            // Symlink çözümlenemezse güvenli tarafı seç: reddet
            return false;
        }

        return true;
    }

    /// <summary>
    /// Bir yolun sembolik bağlantı (symlink / junction) ise gerçek hedefini döndürür.
    /// Sembolik bağlantı değilse null döner.
    /// returnFinalTarget: true olduğundan zincirlenmiş sembolik bağlantılar da tamamen çözülür.
    /// </summary>
    private static string? ResolveSymlinkTarget(string path)
    {
        try
        {
            // Dosya mı klasör mü diye kontrol et
            if (File.Exists(path))
            {
                var fileInfo = new FileInfo(path);
                var target = fileInfo.ResolveLinkTarget(returnFinalTarget: true);
                return target?.FullName;
            }

            if (Directory.Exists(path))
            {
                var dirInfo = new DirectoryInfo(path);
                var target = dirInfo.ResolveLinkTarget(returnFinalTarget: true);
                return target?.FullName;
            }

            // Henüz oluşturulmamış yollar için null dön (symlink değil sayılır)
            return null;
        }
        catch
        {
            return null;
        }
    }

    private bool IsIgnoredPath(string path)
    {

        var ignoredDirs = new[] { ".git", "node_modules", "build", ".dart_tool", "dist", "bin", "obj", ".vs", "packages" };
        var ignoredExtensions = new[] { ".png", ".jpg", ".jpeg", ".pdf", ".dll", ".exe", ".bin", ".zip", ".tar", ".gz" };

        foreach (var dir in ignoredDirs)
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}{dir}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith($"{Path.DirectorySeparatorChar}{dir}", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var ext = Path.GetExtension(path);
        if (!string.IsNullOrEmpty(ext) && ignoredExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Wrapper for BuildProject that includes safety/confirmation checks.
    /// </summary>
    private async Task<ToolResult> SafelyBuildProjectAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (_safeAutomationEnabled)
        {
            var confirmRes = await InvokeConfirmAsync("AI proje derlemesi başlatmak istiyor. Bu işlem güvenli modda kullanıcı onayı gerektirir. Onaylıyor musun?");
            if (confirmRes == ConfirmResult.Skip || confirmRes == ConfirmResult.Cancel)
                return new ToolResult { Success = false, Error = "Derleme kullanıcı onayıyla durduruldu." };
            if (confirmRes == ConfirmResult.DryRun)
                return new ToolResult { Success = true, Output = "Dry run: derleme simülasyonu tamamlandı." };
        }

        return await _buildService!.BuildProjectAsync(arguments, cancellationToken);
    }

    /// <summary>
    /// Wrapper for RunTests that includes safety/confirmation checks.
    /// </summary>
    private async Task<ToolResult> SafelyRunTestsAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (_safeAutomationEnabled)
        {
            var confirmRes = await InvokeConfirmAsync("AI testleri çalıştırmak istiyor. Bu işlem güvenli modda kullanıcı onayı gerektirir. Onaylıyor musun?");
            if (confirmRes == ConfirmResult.Skip || confirmRes == ConfirmResult.Cancel)
                return new ToolResult { Success = false, Error = "Test çalıştırması kullanıcı onayıyla durduruldu." };
            if (confirmRes == ConfirmResult.DryRun)
                return new ToolResult { Success = true, Output = "Dry run: test simülasyonu tamamlandı." };
        }

        return await _buildService!.RunTestsAsync(arguments, cancellationToken);
    }
}

public static class ProjectTypeDetector
{
    public static string DetectProjectType(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return "General";

        if (File.Exists(Path.Combine(folder, "pubspec.yaml")))
            return "Flutter";

        if (File.Exists(Path.Combine(folder, "package.json")))
            return "Node";

        try
        {
            if (Directory.EnumerateFiles(folder, "*.csproj", SearchOption.AllDirectories).Take(1).Any() ||
                Directory.EnumerateFiles(folder, "*.sln", SearchOption.AllDirectories).Take(1).Any())
                return "DotNet";

            if (File.Exists(Path.Combine(folder, "requirements.txt")) ||
                File.Exists(Path.Combine(folder, "pyproject.toml")) ||
                Directory.EnumerateFiles(folder, "*.py", SearchOption.AllDirectories).Take(1).Any())
                return "Python";

            if (File.Exists(Path.Combine(folder, "Cargo.toml")))
                return "Rust";

            if (File.Exists(Path.Combine(folder, "go.mod")))
                return "Go";

            if (File.Exists(Path.Combine(folder, "CMakeLists.txt")) ||
                Directory.EnumerateFiles(folder, "*.cpp", SearchOption.AllDirectories).Take(1).Any())
                return "C/C++";

            if (Directory.EnumerateFiles(folder, "*.html", SearchOption.TopDirectoryOnly).Take(1).Any())
                return "Web";
        }
        catch
        {
            // Ignore IO/permission exceptions during subdirectory enumeration
        }

        return "General";
    }
}
