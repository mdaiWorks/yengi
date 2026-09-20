using System.Collections.ObjectModel;

using System.IO;

using System.Linq;

using System.Reflection;

using System.Text.Json;

using System.Text.RegularExpressions;

using System.Windows;

using System.Windows.Controls;

using System.Windows.Controls.Primitives;

using System.Windows.Documents;

using System.Windows.Input;

using System.Windows.Media;

using System.Windows.Media.Effects;

using System.Windows.Threading;

using ICSharpCode.AvalonEdit;

using ICSharpCode.AvalonEdit.Highlighting;

using ICSharpCode.AvalonEdit.Highlighting.Xshd;

using ICSharpCode.AvalonEdit.Search;

using ICSharpCode.AvalonEdit.Rendering;

using ICSharpCode.AvalonEdit.Folding;

using ICSharpCode.AvalonEdit.Document;

using System.Diagnostics;

using Microsoft.Win32;

using mdaiAgent.Services;

namespace mdaiAgent;

/// <summary>

/// Süslü parantezler için basit folding stratejisi

/// </summary>

public class BraceFoldingStrategy

{

    public void UpdateFoldings(FoldingManager manager, TextDocument document)

    {

        var foldings = new System.Collections.Generic.List<NewFolding>();

        var stack = new System.Collections.Generic.Stack<int>();

        try

        {

            for (int i = 0; i < document.TextLength; i++)

            {

                char c = document.GetCharAt(i);

                if (c == '{')

                {

                    stack.Push(i);

                }

                else if (c == '}' && stack.Count > 0)

                {

                    int startOffset = stack.Pop();

                    int startLine = document.GetLineByOffset(startOffset).LineNumber;

                    int endLine = document.GetLineByOffset(i).LineNumber;

                    if (startLine < endLine)

                    {

                        var folding = new NewFolding(startOffset, i + 1);

                        folding.Name = "...";

                        foldings.Add(folding);

                    }

                }

            }

            foldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));

            manager.UpdateFoldings(foldings, 0);

        }

        catch

        {

            // Hata olursa hiç folding güncelleme

        }

    }

}

public partial class MainWindow : Window

{

    private string? _selectedFolder;

    public string? SelectedFolder => _selectedFolder;

    private string? _currentOpenFile;
    private bool _isSplitEditorActive;

    private AppSettings _settings;

    private IAiProvider? _apiClient;

    private ToolExecutor? _toolExecutor;

    private bool _safeAutomationEnabled;

    private string? _lastOperationStep;

    private TerminalService? _terminalService;

    private AiActionService? _aiActionService;

    private PreviewService? _previewService;

    private PreviewWindow? _previewWindow;

    private DiagnosticsWindow? _diagnosticsWindow;

    private bool _bottomPanelExpanded;

    private VoiceCommandService? _voiceCommandService;

    private IProviderService? _providerService;

    private RagService? _ragService;

    private ProjectWatcherService? _projectWatcherService;

    private readonly List<CommandPaletteItem> _commandPaletteItems = new();

    private readonly List<CommandPaletteItem> _filteredPaletteItems = new();

    private readonly NotificationService _notificationService = new();

    private readonly ToastService _toastService = new();

    private readonly ChatSessionService _chatSessionService;

    private readonly ChatFlowService _chatFlowService;

    private readonly IPlanModeService _planModeService;

    private bool _isPlanModeRunning;

    private PlanModeResult? _cachedPlanResult;   // son üretilen plan — yeniden açılınca tekrar üretilmez

    private const int MaxChatMessagesPerLoad = 100;

    private int _loadedMessageCount = 0;

    private Button? _loadMoreButton;

    private readonly Dictionary<string, bool> _fileErrorStates = new();

    private readonly Dictionary<string, IReadOnlyList<LanguageDiagnostic>> _fileDiagnostics = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, IReadOnlyList<LanguageDiagnostic>> _lspDiagnostics = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<Attachment> _pendingAttachments = new();

    private readonly GitService _gitService = new();

    private readonly ObservableCollection<GitFileChange> _gitChanges = new();

    private System.Timers.Timer? _autoSaveTimer;

    private readonly Dictionary<string, LanguageServerService> _languageServerServices = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, int> _documentVersions = new(StringComparer.OrdinalIgnoreCase);

    private ChatPanelViewModel _chatPanelViewModel = null!;

    private List<CompletionItem> _currentCompletionItems = new();

    private int _selectedCompletionIndex = 0;

    private class CommandPaletteItem

    {

        public string Label { get; set; } = "";

        public string Description { get; set; } = "";

        public string ActionId { get; set; } = "";

        public string? Argument { get; set; }

    }

    private static IHighlightingDefinition? _customHtmlHighlighting;

    public MainWindow()

    {

        InitializeComponent();

        LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
        SettingsWindow.SettingsSaved += (s, e) =>
        {
            _settings = SettingsWindow.GetSettings();
            UpdateMultiAgentButtonUI();
        };

        _settings = SettingsWindow.GetSettings();

        // Set initial workspace mode selection
        foreach (ComboBoxItem item in cmbWorkspaceMode.Items)
        {
            if (item.Tag?.ToString() == _settings.ActiveWorkspaceMode.ToString())
            {
                cmbWorkspaceMode.SelectedItem = item;
                break;
            }
        }

        Localization.SetLanguage(_settings.Language);

        ApplyUiLanguage();

        // Statik terminal mesajı referansını ayarla

        _addTerminalMessage = AddTerminalMessage;

        // Agents checkboxes are now managed in the popup

        _chatSessionService = new ChatSessionService(() => _selectedFolder);

        _chatFlowService = new ChatFlowService(_chatSessionService, AddTerminalMessage, Notify, UpdateOperationStep);

        InitializeApiClient();

        InitializeToolExecutor();

        _chatFlowService.UpdateClients(_apiClient, _toolExecutor);

        // Initialize AiPlanGenerator for isolated plan generation

        AiPlanGenerator? aiPlanGenerator = null;

        if (_apiClient != null)

        {

            try

            {

                aiPlanGenerator = new AiPlanGenerator(_apiClient, AddTerminalMessage);

            }

            catch (Exception ex)

            {

                AddTerminalMessage($"⚠️ AiPlanGenerator başlatma hatası: {ex.Message}");

            }

        }

        // Create PlanModeService with AiPlanGenerator

        _planModeService = aiPlanGenerator != null 

            ? new PlanModeService(aiPlanGenerator, _chatFlowService)

            : new PlanModeService(_chatFlowService);

        InitializeNotificationService();

        InitializeToastOverlay();

        InitializeCommandPalette();

        InitializeTerminalService();

        InitializeAiActionService();

        TokenTrackerService.Instance.OnUsageUpdated += UpdateTokenTrackerUI;

        // Initialize preview service

        _previewService = new PreviewService();

        _previewService.PreviewUrlChanged += OnPreviewUrlChanged;

        _previewService.PreviewStatusChanged += OnPreviewStatusChanged;

        _previewService.PreviewError += OnPreviewError;

        // Initialize voice command service

        InitializeVoiceCommandService();

        // Initialize RAG and ProjectWatcher services

        _ = InitializeRagServicesAsync();

        _chatFlowService.SessionsUpdated += RefreshChatSessionsList;

        _chatFlowService.ActiveSessionChanged += () => 

        {

            RefreshChatSessionsList();

            UpdateChatHistory();

        };

        _chatFlowService.FileModifiedByAi += (filePath) => 

        {

            ReloadTabIfOpen(filePath);

            if (!string.IsNullOrEmpty(_selectedFolder))

            {

                // Refresh tree on background thread or dispatcher to avoid locking

                _ = Dispatcher.InvokeAsync(() => LoadFileTree(_selectedFolder));

            }

        };

        // Git initialization

            lstGitChanges.ItemsSource = _gitChanges;

            tcLeftPanel.SelectionChanged += TcLeftPanel_SelectionChanged;

            _gitService.GitStatusChanged += GitService_GitStatusChanged;

            // Set initial visibility for left panel (Files tab)

            brFilesHeader.Visibility = Visibility.Visible;

            brGitHeader.Visibility = Visibility.Collapsed;

            brSearchBar.Visibility = Visibility.Visible;

            tvFileTree.Visibility = Visibility.Visible;

            grGitPanel.Visibility = Visibility.Collapsed;

        // Initialize persistent logger

        try { Logger.Init(); Logger.LogInfo("Yengi starting"); } catch { }

        AddTerminalMessage(Localization.Get("Yengi başlatıldı.", "Yengi started."));

        _chatFlowService.Initialize();

        RefreshChatSessionsList();

        // Initialize Chat Panel ViewModel and EventBus

        InitializeChatPanelViewModel();

        // Restore previous session after crash

        App.RestoreSessionState(this);

        // Periodic session state save (every 30 seconds)

        var sessionSaveTimer = new System.Timers.Timer(30000);

        sessionSaveTimer.Elapsed += (s, e) => App.SaveSessionState(this);

        sessionSaveTimer.Start();

        // Auto Save Timer (Dosyaları otomatik kaydetme)

        UpdateAutoSaveTimer();

        // Save state before closing

        Closing += (s, e) => 

        { 

            LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged;

            App.SaveSessionState(this); 

            DisposeLanguageServerServices();

            _autoSaveTimer?.Dispose();

            _previewService?.StopPreview();

        };

        // end of constructor — start background update check after 5 seconds
        Loaded += (_, _) => _ = Task.Run(async () =>
        {
            await Task.Delay(5000);
            await CheckForUpdateInBackgroundAsync();
        });

    }

    private UpdateInfo? _pendingUpdateInfo;

    private async Task CheckForUpdateInBackgroundAsync()
    {
        try
        {
            var update = await UpdateService.Instance.CheckForUpdateAsync();
            if (update == null) return;

            _pendingUpdateInfo = update;

            await Dispatcher.InvokeAsync(() =>
            {
                txtUpdateDetails.Text = $"v{update.Version}" + (string.IsNullOrWhiteSpace(update.Changelog) ? "" : $" — {update.Changelog}");
                updateBanner.Visibility = Visibility.Visible;
            });
        }
        catch { /* Güncelleme kontrolü başarısız oldu — sessizce yut */ }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WPF event handler")]
    private async void BtnInstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdateInfo == null) return;

        btnInstallUpdate.IsEnabled = false;
        btnDismissUpdate.IsEnabled = false;
        pbUpdateProgress.Visibility = Visibility.Visible;

        var progress = new Progress<int>(pct =>
        {
            pbUpdateProgress.Value = pct;
        });

        try
        {
            await UpdateService.Instance.DownloadAndInstallUpdateAsync(_pendingUpdateInfo, progress, this);
        }
        catch (Exception ex)
        {
            pbUpdateProgress.Visibility = Visibility.Collapsed;
            btnInstallUpdate.IsEnabled = true;
            btnDismissUpdate.IsEnabled = true;
            MessageBox.Show(
                Localization.IsEnglish()
                    ? $"Update failed: {ex.Message}"
                    : $"Güncelleme başarısız: {ex.Message}",
                "Yengi Update",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void BtnDismissUpdate_Click(object sender, RoutedEventArgs e)
    {
        updateBanner.Visibility = Visibility.Collapsed;
    }

    private LanguageServerService GetLanguageServerService(string languageExtension)
    {
        if (_languageServerServices.TryGetValue(languageExtension, out var existingService))
            return existingService;

        var service = new LanguageServerService();
        service.LogReceived += (s, log) => AddTerminalMessage($"[LSP] {log}");
        service.DiagnosticsReceived += OnLspDiagnosticsReceived;
        _languageServerServices[languageExtension] = service;
        return service;
    }

    private void DisposeLanguageServerServices()
    {
        foreach (var service in _languageServerServices.Values)
            service.Dispose();

        _languageServerServices.Clear();
    }

    private static string GetLspLanguageExtension(string filePath)
    {
        return LspLanguageRegistry.NormalizeExtension(Path.GetExtension(filePath));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)

    {

        if (_toolsPopup != null)
        {
            _toolsPopup.IsOpen = false;
            _toolsPopup = null;
        }

        if (_agentsPopup != null)
        {
            _agentsPopup.IsOpen = false;
            _agentsPopup = null;
        }

        _settings.Language = LocalizationManager.Instance.CurrentLanguageCode;

        ApplyUiLanguage();

    }

    private void ApplyUiLanguage()

    {

        if (btnMultiAgentToggle != null)
            btnMultiAgentToggle.ToolTip = LocalizationManager.Instance.GetString("MultiAgentToggleToolTip");

        UpdateMultiAgentButtonUI();

        if (fileTreeMenuRefresh != null)
            fileTreeMenuRefresh.Header = LocalizationManager.Instance.GetString("FileTreeRefresh");

        if (fileTreeMenuNewFile != null)
            fileTreeMenuNewFile.Header = LocalizationManager.Instance.GetString("FileTreeNewFile");

        if (fileTreeMenuNewFolder != null)
            fileTreeMenuNewFolder.Header = LocalizationManager.Instance.GetString("FileTreeNewFolder");

        if (fileTreeMenuDelete != null)
            fileTreeMenuDelete.Header = LocalizationManager.Instance.GetString("FileTreeDelete");

        if (fileTreeMenuShowInExplorer != null)
            fileTreeMenuShowInExplorer.Header = LocalizationManager.Instance.GetString("FileTreeShowInExplorer");

        if (tcLeftPanel.Items.Count >= 2)

        {
            ((TabItem)tcLeftPanel.Items[0]).Header = LocalizationManager.Instance.GetString("UiFilesTab");

            ((TabItem)tcLeftPanel.Items[1]).Header = "🐙 Git";

        }

        if (txtGitStatusHeader != null)

            txtGitStatusHeader.Text = LocalizationManager.Instance.GetString("UiGitStatus");

        if (btnShowBackups != null)

            btnShowBackups.Content = LocalizationManager.Instance.GetString("UiBackups");

        if (btnGitRefresh != null)

            btnGitRefresh.Content = LocalizationManager.Instance.GetString("UiRefresh");

        if (txtRagStatusTitle != null)

            txtRagStatusTitle.Text = LocalizationManager.Instance.GetString("UiRagDisabled");

        if (btnRagSettings != null)

            btnRagSettings.Content = LocalizationManager.Instance.GetString("UiSettings");

        if (txtWelcomeTitle != null)

            txtWelcomeTitle.Text = LocalizationManager.Instance.GetString("MdaiAgentAHosGeldiniz");

        if (txtWelcomeSubtitle != null)

            txtWelcomeSubtitle.Text = LocalizationManager.Instance.GetString("AIDestekliKodAsistaninizCalismayaHazir");

        if (txtQuickStartTitle != null)

            txtQuickStartTitle.Text = LocalizationManager.Instance.GetString("HizliBaslangic");

        if (txtQuickStart1 != null)

            txtQuickStart1.Text = LocalizationManager.Instance.GetString("QuickStep1");

        if (txtQuickStart3 != null)

            txtQuickStart3.Text = LocalizationManager.Instance.GetString("QuickStep3");

        if (txtSearchFiles != null)

            txtSearchFiles.ToolTip = LocalizationManager.Instance.GetString("UiSearchFilesTooltip");

        if (btnSearchFiles != null)

            btnSearchFiles.ToolTip = LocalizationManager.Instance.GetString("UiStartSearchTooltip");

        if (btnClearSearch != null)

            btnClearSearch.ToolTip = LocalizationManager.Instance.GetString("UiClearSearchTooltip");

        if (btnGitHubSync != null)

            btnGitHubSync.ToolTip = LocalizationManager.Instance.GetString("UiGitHubSyncTooltip");

        if (txtChatListTitle != null)

            txtChatListTitle.Text = LocalizationManager.Instance.GetString("UiChats");

        if (btnNewChatMain != null)

            btnNewChatMain.Content = LocalizationManager.Instance.GetString("UiNewChat");

        if (txtNoProjectWarning != null)

            txtNoProjectWarning.Text = LocalizationManager.Instance.GetString("UiNoProjectWarning");

        if (txtInlineEditTitle != null)

            txtInlineEditTitle.Text = LocalizationManager.Instance.GetString("UiInlineEditTitle");

        if (tbInlineEditStatus != null)

            tbInlineEditStatus.Text = LocalizationManager.Instance.GetString("UiInlineEditStatus");

        if (tbCurrentFile != null)

            tbCurrentFile.Text = LocalizationManager.Instance.GetString("UiNoFileOpen");

        UpdateTerminalPrompt();

        if (tbTokenModel != null)

            tbTokenModel.Text = LocalizationManager.Instance.GetString("UiUnknown");

        if (tbTokenSession != null)

            tbTokenSession.Text = LocalizationManager.Instance.GetString("UiZeroTokens");

        if (tbTokenTotal != null)

            tbTokenTotal.Text = LocalizationManager.Instance.GetString("UiZeroTokens");

        UpdateLspStatusBar(_currentOpenFile);

    }

    private void UpdateTerminalPrompt()
    {
        if (tbTerminalPrompt == null)
            return;

        var workingDir = !string.IsNullOrWhiteSpace(_selectedFolder) && Directory.Exists(_selectedFolder)
            ? _selectedFolder
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var envSegment = Directory.Exists(Path.Combine(workingDir, ".venv"))
            ? "(.venv) "
            : string.Empty;

        tbTerminalPrompt.Text = $"{envSegment}PS {workingDir}>";
    }

    // Cache to avoid customizing the same highlighting definition multiple times

    private static readonly HashSet<object> _customizedHighlightings = new();

    // Terminal mesajı eklemek için statik referans

    private static Action<string>? _addTerminalMessage;

    // Method to load custom HTML highlighting manually from file

    private static void LoadCustomHtmlHighlighting()

    {

        try

        {

            System.Diagnostics.Debug.WriteLine("=== LoadCustomHtmlHighlighting BAŞLADI ===");

            // Dosya yolunu bulalım

            var exePath = AppDomain.CurrentDomain.BaseDirectory;

            var xshdPath = Path.Combine(exePath, "Themes", "Highlighting", "HTML.xshd");

            System.Diagnostics.Debug.WriteLine($"Aranan dosya yolu: {xshdPath}");

            if (File.Exists(xshdPath))

            {

                System.Diagnostics.Debug.WriteLine("=== HTML.xshd DOSYASI BULUNDU! ===");

                using var reader = new System.Xml.XmlTextReader(xshdPath);

                var xshd = HighlightingLoader.LoadXshd(reader);

                System.Diagnostics.Debug.WriteLine($"XSHD yüklendi: {xshd.Name}");

                _customHtmlHighlighting = HighlightingLoader.Load(xshd, null);

                System.Diagnostics.Debug.WriteLine("=== ✅ ÖZEL HTML VURGULAMASI BAŞARIYLA YÜKLENDİ! ===");

                // XSHD içindeki renkleri gösterelim

                foreach (var color in xshd.Elements)

                {

                    if (color is XshdColor xshdColor)

                    {

                        System.Diagnostics.Debug.WriteLine($"XshdColor: Name={xshdColor.Name}, Foreground={xshdColor.Foreground}");

                    }

                }

            }

            else

            {

                System.Diagnostics.Debug.WriteLine($"!!! HATA: {xshdPath} dosyası BULUNAMADI! !!!");

            }

        }

        catch (Exception ex)

        {

            System.Diagnostics.Debug.WriteLine($"!!! LoadCustomHtmlHighlighting HATASI: {ex.Message}");

            System.Diagnostics.Debug.WriteLine($"Stack Trace: {ex.StackTrace}");

        }

        System.Diagnostics.Debug.WriteLine("=== LoadCustomHtmlHighlighting BİTTİ ===");

    }

    // Method to clear the highlighting cache (so new colors take effect)

    public static void ClearHighlightingCache()

    {

        _customizedHighlightings.Clear();

    }

    private static void CustomizeSyntaxHighlighting(IHighlightingDefinition? highlighting)

    {

        _addTerminalMessage?.Invoke($"🔧 Renk özelleştirme başladı: {highlighting?.Name ?? "varsayılan"}");

        if (highlighting == null || _customizedHighlightings.Contains(highlighting)) 

        {

            _addTerminalMessage?.Invoke("⏭️ Atlandı: null veya zaten özelleştirilmiş");

            return;
        }

        var colorMap = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            ["String Text"] = Colors.Orange,

            ["Number"] = Colors.LightGreen,                       // Light green for numbers

            ["Number Literal"] = Colors.LightGreen,

            ["Keyword"] = Colors.HotPink,                         // Hot pink for keywords

            ["Keyword Control"] = Colors.HotPink,

            ["Operator"] = Colors.Yellow,                         // Bright yellow for operators

            ["Punctuation"] = Colors.White,                       // White for punctuation

            ["Identifier"] = Colors.White,                        // White for identifiers

            ["Function"] = Colors.Gold,                           // Gold for functions

            ["Class"] = Colors.Cyan,                              // Cyan for class names

            ["XmlAttribute"] = Colors.White,                      // Pure white for XML attributes (lang, charset...)

            ["XmlAttributeValue"] = Colors.Yellow,                // Bright yellow for attribute values ("tr", "UTF-8")

            ["XmlAttribute Value"] = Colors.Yellow,

            ["XmlTag"] = Colors.Red,                              // Bright red for XML tags

            ["Xml Tag"] = Colors.Red,

            ["TagName"] = Colors.Cyan,

            ["XmlName"] = Colors.Cyan,                            // Bright cyan for XML tag names (html, head...)

            ["Xml Name"] = Colors.Cyan,

            ["XmlEntity"] = Colors.White,

            ["CData"] = Colors.Yellow,

            ["DocType"] = Colors.Cyan,

            ["DOCTYPE"] = Colors.Cyan,

            ["Text"] = Colors.White,

            ["Normal Text"] = Colors.White

        };

        _addTerminalMessage?.Invoke($"🎨 Renk haritası hazır: {colorMap.Count} adet renk");

        // Process rule sets to find any colors (using reflection)

        ProcessObject(highlighting, colorMap, new HashSet<object>());

        _addTerminalMessage?.Invoke("✅ Renk özelleştirme tamamlandı!");

    }

    private static void ProcessObject(object obj, Dictionary<string, Color> colorMap, HashSet<object> visited)

    {

        if (obj == null || visited.Contains(obj)) return;

        visited.Add(obj);

        var type = obj.GetType();

        // Check if this is a HighlightingColor object and update it

        var colorProp = type.GetProperty("Foreground");

        if (colorProp != null)

        {

            // Try to find a name for this color (Name property)

            var nameProp = type.GetProperty("Name");

            string? colorName = nameProp?.GetValue(obj) as string;

            _addTerminalMessage?.Invoke($"  🔍 İşlenen renk: '{colorName}' (tip: {type.Name})");

            if (!string.IsNullOrEmpty(colorName) && colorMap.TryGetValue(colorName, out var newColor))

            {

                _addTerminalMessage?.Invoke($"  ✅ Değiştirildi: {colorName} -> {newColor}");

                var newBrush = new SimpleHighlightingBrush(newColor);

                colorProp.SetValue(obj, newBrush);

            }

        }

        // Process all properties

        foreach (var prop in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))

        {

            try

            {

                var value = prop.GetValue(obj);

                if (value == null) continue;

                if (value is System.Collections.IEnumerable enumerable && !(value is string))

                {

                    foreach (var item in enumerable)

                    {

                        if (item != null) ProcessObject(item, colorMap, visited);

                    }

                }

                else if (value.GetType().IsClass && !value.GetType().IsValueType && !value.GetType().IsPrimitive)

                {

                    ProcessObject(value, colorMap, visited);

                }

            }

            catch { }

        }

        // Also process all fields just in case

        foreach (var field in type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))

        {

            try

            {

                var value = field.GetValue(obj);

                if (value == null) continue;

                if (value is System.Collections.IEnumerable enumerable && !(value is string))

                {

                    foreach (var item in enumerable)

                    {

                        if (item != null) ProcessObject(item, colorMap, visited);

                    }

                }

                else if (value.GetType().IsClass && !value.GetType().IsValueType && !value.GetType().IsPrimitive)

                {

                    ProcessObject(value, colorMap, visited);

                }

            }

            catch { }

        }

    }

    private void SaveSettings()

    {

        // Save _settings to disk

        SettingsWindow.SaveSettings(_settings);

    }

    // Tool açıklamaları sözlüğü

    private static readonly Dictionary<string, (string Icon, string TurkishDescription, string EnglishDescription)> ToolMeta = new()

    {

    ["ReadFile"]              = ("📄", "Projedeki herhangi bir dosyanın içeriğini okur", "Reads any file in the project"),

    ["CreateOrUpdateFile"]    = ("✏️", "Yeni dosya oluşturur veya mevcut dosyayı günceller", "Creates a new file or updates an existing file"),

    ["ReplaceFileContent"]    = ("✂️", "Dosyanın belirli bir bölümünü değiştirir", "Replaces a specific section of a file"),

        ["ExecuteTerminalCommand"] = ("💻", "Terminal/shell komutu çalıştırır", "Runs a terminal or shell command"),

    ["FindFiles"]             = ("🔍", "Dosya/klasör isimlerinde arama yapar", "Searches file and folder names"),

    ["SearchCode"]            = ("🔎", "Dosya içeriklerinde metin/kod arar", "Searches text and code in file contents"),

    ["ListDirectory"]         = ("📁", "Klasör içeriğini listeler", "Lists the contents of a folder"),

    ["BuildProject"]          = ("🔨", "Projeyi derler", "Builds the project"),

    ["RunTests"]              = ("🧪", "Birim testlerini çalıştırır", "Runs unit tests"),

    ["CreatePlan"]            = ("📋", "Büyük görevler için adım adım plan yapar", "Creates a step-by-step plan for large tasks"),

    ["GenerateDiff"]          = ("📝", "Yapılan kod değişikliklerinin önizlemesini sunar", "Previews the code changes made"),

    ["ReadProjectMemory"]     = ("🧠", "Projenin hafızasından genel kuralları okur", "Reads general rules from project memory"),

    ["WriteProjectMemory"]    = ("🧠", "Mimari veya kodlama kuralını kalıcı hafızaya kaydeder", "Saves an architecture or coding convention to project memory"),

    ["SearchProjectMemory"]   = ("🔍", "Kalıcı proje hafızasında arama yapar", "Searches the persistent project memory"),

    ["ArchiveProjectMemory"]  = ("🗄️", "Geçersiz hafıza kaydını arşivler", "Archives an obsolete memory entry"),

    ["WriteDecision"]         = ("⚖️", "Alınan mimari kararı ve gerekçesini karar günlüğüne kaydeder", "Records an architectural decision and its rationale"),

    ["WriteTask"]              = ("✅", "Uzun görevlerin durumunu ve ilerleme notlarını kaydeder", "Records the status and progress of a long-running task"),

    ["RetryPlan"]             = ("🔄", "Hata durumunda kurtarma planı yapar", "Creates a recovery plan after an error"),

    ["TakeScreenshot"]        = ("📷", "Uygulamanın/ekranın görüntüsünü alır", "Captures a screenshot of the app or screen"),

    ["DelegateTask"]          = ("🤝", "Görevi başka bir AI alt ajana devreder", "Delegates the task to another AI subagent"),

    ["AskUserOptions"]        = ("❓", "Kullanıcıya popup penceresinde soru sorar", "Asks the user a question in a popup"),

    ["CreateCheckpoint"]      = ("💾", "Geri dönülebilir bir proje kayıt noktası oluşturur", "Creates a reversible project checkpoint"),

    ["RollbackToCheckpoint"]  = ("⏪", "Projeyi eski bir kayıt noktasına geri döndürür", "Rolls the project back to an earlier checkpoint"),

    ["CreateTaskGraph"]       = ("🔀", "Görevin bağımlılık grafiğini çıkarır", "Builds the task dependency graph"),

        ["DiscoverProjectContext"] = ("🧭", "Proje yapısını, teknoloji türünü ve önemli dosyaları keşfeder", "Discovers the project structure, technology, and key files"),

    ["CreateQuickCommand"]    = ("⚡", "Tekrar kullanılmak üzere hızlı bir terminal komutu kaydeder", "Saves a quick terminal command for reuse"),

    ["ExecuteQuickCommand"]   = ("🚀", "Daha önce kaydedilmiş hızlı komutu yürütür", "Executes a previously saved quick command"),

    ["WebSearch"]             = ("🌐", "İnternet üzerinde arama yapar", "Searches the internet"),

    ["WebFetch"]              = ("📥", "Belirtilen bir URL'in içeriğini okur", "Reads the content of a specified URL")

    };

    private static readonly Dictionary<string, string> LocalizedToolDescriptionKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ReadProjectMemory"] = "ToolDescriptionReadProjectMemory",
        ["WriteProjectMemory"] = "ToolDescriptionWriteProjectMemory",
        ["SearchProjectMemory"] = "ToolDescriptionSearchProjectMemory",
        ["ArchiveProjectMemory"] = "ToolDescriptionArchiveProjectMemory",
        ["WriteDecision"] = "ToolDescriptionWriteDecision",
        ["WriteTask"] = "ToolDescriptionWriteTask"
    };

    private Popup? _toolsPopup;

    private void BtnTools_Click(object sender, RoutedEventArgs e)

    {

        if (_toolsPopup?.IsOpen == true)
        {
            _toolsPopup.IsOpen = false;
            return;
        }

        _toolsPopup = BuildToolsPopup();

        _toolsPopup.PlacementTarget = btnTools;

        _toolsPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;

        _toolsPopup.IsOpen = true;

    }

    private static string GetToolModeDescription(bool isManual)
    {
        return isManual
            ? Localization.Get("Araçları kendin seç. Sadece açık araçlar modele gider.", "Select tools yourself. Only enabled tools are sent to the model.")
            : Localization.Get("Model / Router karar verir. Tüm araçlar gönderilir veya Router seçer.", "The model / Router decides. All tools are sent or the Router selects.");
    }

    private Popup BuildToolsPopup()

    {

        var popup = new Popup

        {

            StaysOpen = false,

            AllowsTransparency = true,

            PopupAnimation = PopupAnimation.Slide,

        };

        var outerBorder = new Border

        {

            Background  = new SolidColorBrush(Color.FromRgb(0x13, 0x13, 0x22)),

            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2e, 0x2e, 0x50)),

            BorderThickness = new Thickness(1),

            CornerRadius    = new CornerRadius(10),

            Width = 360,

            MaxHeight = 580,

            Effect = new System.Windows.Media.Effects.DropShadowEffect

            {

                Color = Colors.Black, BlurRadius = 20, ShadowDepth = 0, Opacity = 0.6

            }

        };

        var mainStack = new StackPanel();

        // ── Başlık ──────────────────────────────

        var header = new Border

        {

            Background      = new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x2f)),

            Padding         = new Thickness(16, 12, 16, 12),

            CornerRadius    = new CornerRadius(10, 10, 0, 0),

            BorderBrush     = new SolidColorBrush(Color.FromRgb(0x2e, 0x2e, 0x50)),

            BorderThickness = new Thickness(0, 0, 0, 1)

        };

        var headerStack = new StackPanel();

        headerStack.Children.Add(new TextBlock

        {

            Text       = Localization.Get("🛠️  Araç Yönetimi", "🛠️  Tool Management"),

            FontSize   = 15,

            FontWeight = FontWeights.Bold,

            Foreground = new SolidColorBrush(Colors.White)

        });

        header.Child = headerStack;

        mainStack.Children.Add(header);

        // ── Ana Mod Şalteri ──────────────────────────────

        bool isManual = _settings.IsManualToolManagement;

        var modeSwitchBorder = new Border

        {

            Background      = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x20)),

            Padding         = new Thickness(14, 10, 14, 10),

            BorderBrush     = new SolidColorBrush(Color.FromRgb(0x2e, 0x2e, 0x50)),

            BorderThickness = new Thickness(0, 0, 0, 1),

            Cursor          = Cursors.Hand

        };

        var modeSwitchGrid = new Grid();

        modeSwitchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        modeSwitchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var modeTextStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        var modeTitleBlock = new TextBlock

        {

            FontSize   = 13,

            FontWeight = FontWeights.SemiBold,

            Foreground = new SolidColorBrush(Colors.White),

            Text       = isManual ? "✋  Manuel Mod" : "🤖  Otomatik Mod"

        };

        var modeDescBlock = new TextBlock

        {

            FontSize   = 10,

            Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0xaa)),

            TextWrapping = TextWrapping.Wrap,

            Margin     = new Thickness(0, 2, 0, 0),

            Text       = isManual

                ? "Araçları kendin seç. Sadece açık araçlar modele gider."

                : "Model / Router karar verir. Tüm araçlar gönderilir veya Router seçer."

        };

        modeTitleBlock.Text = isManual
            ? Localization.Get("✋  Manuel Mod", "✋  Manual Mode")
            : Localization.Get("🤖  Otomatik Mod", "🤖  Automatic Mode");
        modeDescBlock.Text = GetToolModeDescription(isManual);
        modeTextStack.Children.Add(modeTitleBlock);

        modeTextStack.Children.Add(modeDescBlock);

        Grid.SetColumn(modeTextStack, 0);

        modeSwitchGrid.Children.Add(modeTextStack);

        // Mod şalteri toggle pill

        var modeActiveBrush   = new SolidColorBrush(Color.FromRgb(0x6e, 0xe7, 0xb7));

        var modeInactiveBrush = new SolidColorBrush(Color.FromRgb(0x3a, 0x3a, 0x5c));

        var modeToggleBorder = new Border

        {

            Width = 44, Height = 24,

            CornerRadius = new CornerRadius(12),

            Background = isManual ? modeActiveBrush : modeInactiveBrush,

            Margin = new Thickness(12, 0, 0, 0),

            VerticalAlignment = VerticalAlignment.Center

        };

        var modeThumb = new System.Windows.Shapes.Ellipse

        {

            Width = 18, Height = 18,

            Fill  = Brushes.White,

            HorizontalAlignment = isManual ? HorizontalAlignment.Right : HorizontalAlignment.Left,

            Margin = isManual ? new Thickness(0, 0, 3, 0) : new Thickness(3, 0, 0, 0)

        };

        modeToggleBorder.Child = modeThumb;

        Grid.SetColumn(modeToggleBorder, 1);

        modeSwitchGrid.Children.Add(modeToggleBorder);

        modeSwitchBorder.Child = modeSwitchGrid;

        mainStack.Children.Add(modeSwitchBorder);

        // ── Araç listesi ────────────────────────

        var scrollViewer = new ScrollViewer

        {

            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,

            MaxHeight = 430,

            Background = Brushes.Transparent

        };

        var itemStack = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };

        var allTools = ToolRegistry.GetTools();

        var toolRows = new List<(Border row, Border toggle, System.Windows.Shapes.Ellipse thumb)>();

        foreach (var tool in allTools)

        {

            var toolName = tool.Function?.Name;

            if (string.IsNullOrEmpty(toolName)) continue;

            var isActive = !_settings.DisabledTools.Contains(toolName);

            var hasMetadata = ToolMeta.TryGetValue(toolName, out var meta);

            var icon = hasMetadata && !string.IsNullOrEmpty(meta.Icon) ? meta.Icon : "🔧";

            var desc = LocalizedToolDescriptionKeys.TryGetValue(toolName, out var descriptionKey)
                ? LocalizationManager.Instance.GetString(descriptionKey)
                : hasMetadata
                    ? (Localization.IsEnglish() ? meta.EnglishDescription : meta.TurkishDescription)
                    : Localization.Get("Araç açıklaması yok", "No tool description");

            // Satır border

            var rowBorder = new Border

            {

                Padding         = new Thickness(14, 9, 14, 9),

                Background      = Brushes.Transparent,

                Cursor          = isManual ? Cursors.Hand : Cursors.Arrow,

                Opacity         = isManual ? 1.0 : 0.4

            };

            // İçerik grid

            var rowGrid = new Grid();

            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var iconBlock = new TextBlock

            {

                Text              = icon,

                FontSize          = 18,

                VerticalAlignment = VerticalAlignment.Center,

                Margin            = new Thickness(0, 0, 10, 0)

            };

            Grid.SetColumn(iconBlock, 0);

            rowGrid.Children.Add(iconBlock);

            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            var nameBlock = new TextBlock

            {

                Text       = toolName,

                FontSize   = 13,

                FontWeight = FontWeights.SemiBold,

                Foreground = new SolidColorBrush(Colors.White)

            };

            var descBlock = new TextBlock

            {

                Text         = desc,

                FontSize     = 10.5,

                Foreground   = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0xaa)),

                TextWrapping = TextWrapping.Wrap,

                Margin       = new Thickness(0, 2, 0, 0)

            };

            textStack.Children.Add(nameBlock);

            textStack.Children.Add(descBlock);

            Grid.SetColumn(textStack, 1);

            rowGrid.Children.Add(textStack);

            var activeBrush   = new SolidColorBrush(Color.FromRgb(0x6e, 0xe7, 0xb7));

            var inactiveBrush = new SolidColorBrush(Color.FromRgb(0x3a, 0x3a, 0x5c));

            var toggleBorder = new Border

            {

                Width  = 44, Height = 24,

                CornerRadius = new CornerRadius(12),

                Background   = isActive ? activeBrush : inactiveBrush,

                Margin       = new Thickness(10, 0, 0, 0),

                VerticalAlignment = VerticalAlignment.Center

            };

            var thumb = new System.Windows.Shapes.Ellipse

            {

                Width  = 18, Height = 18,

                Fill   = Brushes.White,

                HorizontalAlignment = isActive ? HorizontalAlignment.Right : HorizontalAlignment.Left,

                Margin = isActive ? new Thickness(0, 0, 3, 0) : new Thickness(3, 0, 0, 0)

            };

            toggleBorder.Child = thumb;

            Grid.SetColumn(toggleBorder, 2);

            rowGrid.Children.Add(toggleBorder);

            rowBorder.Child = rowGrid;

            toolRows.Add((rowBorder, toggleBorder, thumb));

            // Hover (sadece Manuel modda)

            rowBorder.MouseEnter += (s, ev) =>

            {

                if (!_settings.IsManualToolManagement) return;

                rowBorder.Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x38));

                rowBorder.CornerRadius = new CornerRadius(6);

            };

            rowBorder.MouseLeave += (s, ev) =>

            {

                rowBorder.Background = Brushes.Transparent;

            };

            // Toggle tıklama (sadece Manuel modda)

            rowBorder.MouseLeftButtonUp += (s, ev) =>

            {

                if (!_settings.IsManualToolManagement) return;

                var nowActive = !_settings.DisabledTools.Contains(toolName);

                if (nowActive)

                {

                    _settings.DisabledTools.Add(toolName);

                    toggleBorder.Background = inactiveBrush;

                    thumb.HorizontalAlignment = HorizontalAlignment.Left;

                    thumb.Margin = new Thickness(3, 0, 0, 0);

                }

                else

                {

                    _settings.DisabledTools.Remove(toolName);

                    toggleBorder.Background = activeBrush;

                    thumb.HorizontalAlignment = HorizontalAlignment.Right;

                    thumb.Margin = new Thickness(0, 0, 3, 0);

                }

                SaveSettings();

            };

            itemStack.Children.Add(rowBorder);

        }

        // ── Ana şalter toggle davranışı ──────────────────────────────

        modeSwitchBorder.MouseLeftButtonUp += (s, ev) =>

        {

            if (!_settings.IsManualToolManagement)

            {

                // Kullanıcı Otomatik'ten Manuel'e geçmek istiyor. Router aktif mi kontrol et:

                if (_settings.RouterEnabled || _settings.RouterUseMainModel || _settings.RouterUseLocalModel)

                {

                    MessageBox.Show(LocalizationManager.Instance.GetString("AyarlarMenusundeAIRouterAktifManuelModAGecebilmekIcinLutfenOnceAyarlarDanRouterIKapatin"), LocalizationManager.Instance.GetString("Uyari2"), MessageBoxButton.OK, MessageBoxImage.Warning);

                    return; // Toggle'ı engelle

                }

            }

            _settings.IsManualToolManagement = !_settings.IsManualToolManagement;

            var nowManual = _settings.IsManualToolManagement;

            modeTitleBlock.Text = nowManual
                ? Localization.Get("🛠  Manuel Mod", "🛠  Manual Mode")
                : Localization.Get("🤖  Otomatik Mod", "🤖  Automatic Mode");

            modeDescBlock.Text  = nowManual

                ? "Araçları kendin seç. Sadece açık araçlar modele gider."

                : "Model / Router karar verir. Tüm araçlar gönderilir veya Router seçer.";

            modeDescBlock.Text = GetToolModeDescription(nowManual);
            modeToggleBorder.Background = nowManual ? modeActiveBrush : modeInactiveBrush;

            modeThumb.HorizontalAlignment = nowManual ? HorizontalAlignment.Right : HorizontalAlignment.Left;

            modeThumb.Margin = nowManual ? new Thickness(0, 0, 3, 0) : new Thickness(3, 0, 0, 0);

            foreach (var (row, _, _) in toolRows)

            {

                row.Opacity = nowManual ? 1.0 : 0.4;

                row.Cursor  = nowManual ? Cursors.Hand : Cursors.Arrow;

            }

            SaveSettings();

        };

        scrollViewer.Content = itemStack;

        mainStack.Children.Add(scrollViewer);

        outerBorder.Child = mainStack;

        popup.Child = outerBorder;

        return popup;

    }

    private void BtnClearHistory_Click(object sender, RoutedEventArgs e)

    {

        var result = MessageBox.Show(

            LocalizationManager.Instance.GetString("SohbetGecmisiTemizlenecekProjeAyarlariVeProjeKlasoruKorunacakDevamEdilsinMi"),

            LocalizationManager.Instance.GetString("Temizle"),

            MessageBoxButton.YesNo,

            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        _chatFlowService.ClearActiveSessionHistory();

        // UI'yı yenile — geçmiş mesaj balonları temizlenir

        Dispatcher.Invoke(() =>

        {

            UpdateChatHistory();

            RefreshChatSessionsList();

            AddTerminalMessage(LocalizationManager.Instance.GetString("SohbetGecmisiTemizlendiProjeAyarlariVeBaglamiKorunmaktadir"));

        });

    }

    private Popup? _agentsPopup;

    private void BtnAgents_Click(object sender, RoutedEventArgs e)

    {

        if (_agentsPopup == null)

        {

            _agentsPopup = BuildAgentsPopup();

        }

        _agentsPopup.PlacementTarget = btnAgents;

        _agentsPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;

        _agentsPopup.IsOpen = !_agentsPopup.IsOpen;

    }

    private Popup BuildAgentsPopup()

    {

        var popup = new Popup

        {

            StaysOpen = false,

            AllowsTransparency = true,

            PopupAnimation = PopupAnimation.Slide,

        };

        var outerBorder = new Border

        {

            Background  = new SolidColorBrush(Color.FromRgb(0x13, 0x13, 0x22)),

            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2e, 0x2e, 0x50)),

            BorderThickness = new Thickness(1),

            CornerRadius    = new CornerRadius(10),

            Width = 320,

            Effect = new System.Windows.Media.Effects.DropShadowEffect

            {

                Color = Colors.Black, BlurRadius = 20, ShadowDepth = 0, Opacity = 0.6

            }

        };

        var mainStack = new StackPanel();

        // Üst Başlık

        var header = new Border

        {

            Background      = new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x2f)),

            Padding         = new Thickness(16, 12, 16, 12),

            CornerRadius    = new CornerRadius(10, 10, 0, 0),

            BorderBrush     = new SolidColorBrush(Color.FromRgb(0x2e, 0x2e, 0x50)),

            BorderThickness = new Thickness(0, 0, 0, 1)

        };

        var headerStack = new StackPanel();

        headerStack.Children.Add(new TextBlock

        {

            Text       = Localization.Get("🤖  Uzman Ajanlar", "🤖  Expert Agents"),

            FontSize   = 15,

            FontWeight = FontWeights.Bold,

            Foreground = new SolidColorBrush(Colors.White)

        });

        header.Child = headerStack;

        mainStack.Children.Add(header);

        // Ajanları listeleyeceğimiz Panel

        var agentsStack = new StackPanel { Margin = new Thickness(12) };

        // 1. QA Ajanı

        var qaAgentGrid = CreateAgentItem(
            "🔍",
            LocalizationManager.Instance.GetString("QADenetimAjani"),
            LocalizationManager.Instance.GetString("KodKalitesiniOtomatikDenetlerVeRefaktorOnerir"),
            _settings.QaAgentEnabled, (isChecked) =>

        {

            _settings.QaAgentEnabled = isChecked;

            SaveSettings();

        });

        // 2. UI Ajanı

        var uiAgentGrid = CreateAgentItem(
            "🎨",
            LocalizationManager.Instance.GetString("UIAmpRefaktorAjani"),
            LocalizationManager.Instance.GetString("ArayuzVeDuzenIyilestirmesiYapar"),
            _settings.UiAgentEnabled, (isChecked) =>

        {

            _settings.UiAgentEnabled = isChecked;

            SaveSettings();

        });

        agentsStack.Children.Add(qaAgentGrid);

        agentsStack.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(0x2e, 0x2e, 0x50)), Margin = new Thickness(0, 10, 0, 10) });

        agentsStack.Children.Add(uiAgentGrid);

        mainStack.Children.Add(agentsStack);

        outerBorder.Child = mainStack;

        popup.Child = outerBorder;

        return popup;

    }

    private Grid CreateAgentItem(string icon, string title, string description, bool isEnabled, Action<bool> onToggle)

    {

        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var textStack = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 8, 0) };

        var titleBlock = new TextBlock

        {

            Text = $"{icon}  {title}",

            FontWeight = FontWeights.SemiBold,

            Foreground = new SolidColorBrush(Color.FromRgb(0xe0, 0xe0, 0xe0)),

            FontSize = 14,

            Margin = new Thickness(0, 0, 0, 4)

        };

        var descBlock = new TextBlock

        {

            Text = description,

            Foreground = new SolidColorBrush(Color.FromRgb(0x9a, 0x9a, 0xb0)),

            FontSize = 12,

            TextWrapping = TextWrapping.Wrap,

            Opacity = 0.85

        };

        textStack.Children.Add(titleBlock);

        textStack.Children.Add(descBlock);

        Grid.SetColumn(textStack, 0);

        var toggle = new System.Windows.Controls.Primitives.ToggleButton

        {

            IsChecked = isEnabled,

            Style = (Style)FindResource("AgentToggleButtonStyle"),

            VerticalAlignment = VerticalAlignment.Center

        };

        toggle.Checked += (s, e) => onToggle(true);

        toggle.Unchecked += (s, e) => onToggle(false);

        Grid.SetColumn(toggle, 1);

        grid.Children.Add(textStack);

        grid.Children.Add(toggle);

        return grid;

    }

    private void RunBackground(Task t, string? ctx = null)

    {

        if (t == null) return;

        var _ = t.ContinueWith(tt =>

        {

            try

            {

                var ex = tt.Exception?.Flatten();

                if (ex != null)

                {

                    AddTerminalMessage($"Background task error{(ctx != null ? $" ({ctx})" : string.Empty)}: {ex.Message}");

                }

            }

            catch { }

        }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

    }

    private void AutoSaveModifiedFiles()

    {

        try

        {

            Dispatcher.Invoke(() =>

            {

                foreach (TabItem item in tcEditor.Items)

                {

                    if (item.Content is TextEditor editor && item.Tag is TabInfo tabInfo && tabInfo.IsModified)

                    {

                        try

                        {

                            editor.Save(tabInfo.FilePath);

                            tabInfo.IsModified = false;

                            UpdateTabHeader(item, tabInfo);

                            AddTerminalMessage($"Otomatik kaydedildi: {Path.GetFileName(tabInfo.FilePath)}");

                        }

                        catch (Exception ex)

                        {

                            AddTerminalMessage($"Kaydetme hatası ({tabInfo.FilePath}): {ex.Message}");

                        }

                    }

                }

            });

        }

        catch

        {

            // Hata olursa sessizce geç

        }

    }

    private void TcLeftPanel_SelectionChanged(object sender, SelectionChangedEventArgs e)

    {

        if (tcLeftPanel.SelectedIndex == 1)

        {

            // Git sekmesi seçildi

            brFilesHeader.Visibility = Visibility.Collapsed;

            brGitHeader.Visibility = Visibility.Visible;

            brSearchBar.Visibility = Visibility.Collapsed;

            tvFileTree.Visibility = Visibility.Collapsed;

            lstSearchResultsBorder.Visibility = Visibility.Collapsed;

            grGitPanel.Visibility = Visibility.Visible;

            RefreshGitChanges();

        }

        else

        {

            // Dosyalar sekmesi seçildi

            brFilesHeader.Visibility = Visibility.Visible;

            brGitHeader.Visibility = Visibility.Collapsed;

            brSearchBar.Visibility = Visibility.Visible;

            tvFileTree.Visibility = Visibility.Visible;

            grGitPanel.Visibility = Visibility.Collapsed;

        }

    }

    private void GitService_GitStatusChanged(object? sender, EventArgs e)

    {

        RefreshGitChanges();

    }

    private void RefreshGitChanges()

    {

        if (!Dispatcher.CheckAccess())

        {

            Dispatcher.Invoke(RefreshGitChanges);

            return;

        }

        _gitChanges.Clear();

        var changes = _gitService.GetGitStatus();

        foreach (var change in changes)

        {

            _gitChanges.Add(new GitFileChange { FilePath = change.FilePath, Status = change.Status });

        }

    }

    private void BtnGitRefresh_Click(object sender, RoutedEventArgs e)

    {

        RefreshGitChanges();

    }

#pragma warning disable VSTHRD100

    private async void BtnGitHubSync_Click(object sender, RoutedEventArgs e)

    {

        try

        {

            if (!_gitService.IsGitRepository)

            {

                MessageBox.Show(LocalizationManager.Instance.GetString("BuKlasorBirGitDeposuDegilOnceGitBaslatin"), LocalizationManager.Instance.GetString("Uyari2"), MessageBoxButton.OK, MessageBoxImage.Warning);

                return;

            }

            // 1. Bekleyen değişiklikleri kontrol et ve commit et

            var changes = _gitService.GetGitStatus();

            if (changes.Count > 0)

            {

                foreach (var change in changes)

                {

                    _gitService.StageFile(change.FilePath);

                }

                _gitService.Commit($"Auto-commit before GitHub Sync: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

                AddTerminalMessage(LocalizationManager.Instance.GetString("BekleyenDegisikliklerYedeklendiCommit"));

            }

            // 2. GitHub bilgilerini her zaman göster — mevcut değerler dolu olsa bile kullanıcı güncelleyebilsin

            var existingUser  = _settings.GitHubUsername ?? "";

            var existingToken = _settings.GitHubToken    ?? "";

            var existingUrl   = _gitService.GetRemoteUrl("origin") ?? "";

            // Kullanıcı adı (mevcut değer ön dolu gelsin)

            var userDialog = new InputDialog(LocalizationManager.Instance.GetString("GitHubKullaniciAdiniziGirin"), title: LocalizationManager.Instance.GetString("GitHubKullaniciAdi"), defaultResponse: existingUser);

            userDialog.Owner = this;

            if (userDialog.ShowDialog() == true)

            {

                _settings.GitHubUsername = userDialog.InputText.Trim();

                SaveSettings();

            }

            else return;

            // Token (güvenlik için gösterilmez, boş bırakılırsa eskileri korunur)

            var tokenDialog = new InputDialog(

                "GitHub Personal Access Token girin:\n(Developer Settings → Personal Access Tokens üzerinden alabilirsiniz)\n" +

                "Not: Mevcut token güvenlik için gösterilmez. Değiştirmek istiyorsan yenisini gir, değiştirmeyeceksen boş bırak.",

                title: "GitHub Token",

                defaultResponse: string.Empty);

            tokenDialog.Owner = this;

            if (tokenDialog.ShowDialog() == true)

            {

                var newToken = tokenDialog.InputText.Trim();

                if (!string.IsNullOrWhiteSpace(newToken))

                {

                    _settings.GitHubToken = newToken;

                    SaveSettings();

                }

                else if (string.IsNullOrWhiteSpace(existingToken))

                {

                    MessageBox.Show(LocalizationManager.Instance.GetString("TokenBosBirakilamazIslemIptalEdildi"), LocalizationManager.Instance.GetString("Hata"), MessageBoxButton.OK, MessageBoxImage.Warning);

                    return;

                }

                // else: boş girildi ama zaten token var → eskisi korunuyor

            }

            else return;

            // Remote URL (mevcut URL ön dolu gelsin)

            var urlDialog = new InputDialog("GitHub Repository URL'si (örn: https://github.com/user/repo.git):", title: "GitHub Repository URL", defaultResponse: existingUrl);

            urlDialog.Owner = this;

            if (urlDialog.ShowDialog() == true)

            {

                var newUrl = urlDialog.InputText.Trim();

                if (!string.IsNullOrWhiteSpace(newUrl) && newUrl != existingUrl)

                {

                    _gitService.SetRemoteUrl("origin", newUrl);

                }

                else if (string.IsNullOrWhiteSpace(newUrl) && string.IsNullOrWhiteSpace(existingUrl))

                {

                    MessageBox.Show(LocalizationManager.Instance.GetString("RepositoryURLBosBirakilamazIslemIptalEdildi"), LocalizationManager.Instance.GetString("Hata"), MessageBoxButton.OK, MessageBoxImage.Warning);

                    return;

                }

            }

            else return;

            // 4. Push işlemi

            AddTerminalMessage($"GitHub'a (origin) senkronize ediliyor... Kullanıcı: {_settings.GitHubUsername}");

            UpdateOperationStep("GitHub'a kodlar yükleniyor...");

            // Push işlemi uzun sürebilir, UI kilitlenmesin diye Task.Run içinde yapıyoruz

            var username = _settings.GitHubUsername;

            var token = _settings.GitHubToken;

            bool success = await Task.Run(() => _gitService.PushToRemote("origin", "master", username, token));

            if (!success)

            {

                // Eğer master başarısız olursa main denesin (GitHub varsayılan branch 'main' olabiliyor)

                success = await Task.Run(() => _gitService.PushToRemote("origin", "main", username, token));

            }

            if (success)

            {

                AddTerminalMessage(LocalizationManager.Instance.GetString("GitHubSenkronizasyonuBASARILI"));

                Notify("Kodlar GitHub'a başarıyla gönderildi.", NotificationSeverity.Success);

                UpdateOperationStep("GitHub senkronizasyonu tamamlandı.");

            }

            else

            {

                AddTerminalMessage(LocalizationManager.Instance.GetString("GitHubSenkronizasyonuBASARISIZTokenYetkiVeyaInternetBaglantisiniKontrolEdin"));

                Notify("GitHub'a gönderim başarısız.", NotificationSeverity.Error);

                UpdateOperationStep("");

            }

        }

        catch (Exception ex)

        {

            AddTerminalMessage($"GitHub senkronizasyon hatası: {ex.Message}");

            Notify("GitHub işlemi sırasında hata oluştu.", NotificationSeverity.Error);

            UpdateOperationStep("");

        }

    }

#pragma warning restore VSTHRD100

    private void BtnStageAll_Click(object sender, RoutedEventArgs e)

    {

        try

        {

            var changes = _gitService.GetGitStatus();

            foreach (var change in changes)

            {

                _gitService.StageFile(change.FilePath);

            }

        }

        catch (Exception ex)

        {

            MessageBox.Show(LocalizationManager.Instance.GetString("StageAllHatasiExMessage").Replace("{ex.Message}", ex.Message), LocalizationManager.Instance.GetString("Hata"), MessageBoxButton.OK, MessageBoxImage.Error);

        }

    }

    private void BtnCommit_Click(object sender, RoutedEventArgs e)

        {

            var dialog = new InputDialog("Commit mesajını girin:", "Commit mesajı");

            if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.InputText))

            {

                try

                {

                    _gitService.Commit(dialog.InputText);

                    MessageBox.Show(LocalizationManager.Instance.GetString("CommitBasariylaOlusturuldu"), LocalizationManager.Instance.GetString("Basarili"), MessageBoxButton.OK, MessageBoxImage.Information);

                }

                catch (Exception ex)

                {

                    MessageBox.Show(LocalizationManager.Instance.GetString("CommitHatasiExMessage").Replace("{ex.Message}", ex.Message), LocalizationManager.Instance.GetString("Hata"), MessageBoxButton.OK, MessageBoxImage.Error);

                }

            }

        }

    private void MenuItemGitStage_Click(object sender, RoutedEventArgs e)

    {

        if (lstGitChanges.SelectedItem is GitFileChange change)

        {

            try

            {

                _gitService.StageFile(change.FilePath);

            }

            catch (Exception ex)

            {

                MessageBox.Show(LocalizationManager.Instance.GetString("StageHatasiExMessage").Replace("{ex.Message}", ex.Message), LocalizationManager.Instance.GetString("Hata"), MessageBoxButton.OK, MessageBoxImage.Error);

            }

        }

    }

    private void MenuItemGitUnstage_Click(object sender, RoutedEventArgs e)

    {

        if (lstGitChanges.SelectedItem is GitFileChange change)

        {

            try

            {

                _gitService.UnstageFile(change.FilePath);

            }

            catch (Exception ex)

            {

                MessageBox.Show(LocalizationManager.Instance.GetString("UnstageHatasiExMessage").Replace("{ex.Message}", ex.Message), LocalizationManager.Instance.GetString("Hata"), MessageBoxButton.OK, MessageBoxImage.Error);

            }

        }

    }

    private void InitializeApiClient()

    {

        _apiClient = AiProviderFactory.CreateProvider(_settings);

        AddTerminalMessage(
            LocalizationManager.Instance
                .GetString("APIIstemcisiBaslatildiApiClientModelName")
                .Replace("{_apiClient.ModelName}", _apiClient.ModelName));

    }

    private void InitializeToolExecutor()

    {

        _toolExecutor = new ToolExecutor(

                _selectedFolder,

                AddTerminalMessage,

                async (message) => await RunOnUiThreadAsync(() => ShowConfirmCommandWindowAsync(message)),

                ShowDiffWindowConfirmAsync,

                _safeAutomationEnabled,

                UpdateOperationStep,

                Notify,

                confirmUiInvoker: async operation => await RunOnUiThreadAsync(operation),

                fileChangeUiInvoker: async operation => await RunOnUiThreadAsync(operation),

                settings: _settings,

                askUserOptionsWithSelection: async (question, options, allowMultiple) => await RunOnUiThreadAsync(() => ShowAskUserDialogAsync(question, options, allowMultiple)),

                requestExternalFolderAccess: async folderPath => await RunOnUiThreadAsync(() => ShowExternalFolderAccessAsync(folderPath))

            );

    }

    private void UpdateOperationStep(string step)

    {

        if (!Dispatcher.CheckAccess())

        {

            Dispatcher.Invoke(() => UpdateOperationStep(step));

            return;

        }

        _lastOperationStep = step;

        UpdateProcessStatus(step);
        if (string.IsNullOrWhiteSpace(step))
        {
            agentStatusEllipse.Visibility = Visibility.Collapsed;
            agentStatusText.Visibility = Visibility.Collapsed;
            RemoveActiveProgressStatus();
            return;
        }

        if (!string.IsNullOrWhiteSpace(step))

        {

            UpdateActiveProgressStatus(step);

        }

        try

        {

            // Show agent indicator for agent-related steps

            if (!string.IsNullOrEmpty(step) && (step.Contains("Ajanı", StringComparison.OrdinalIgnoreCase) || step.StartsWith("🔍") || step.StartsWith("🎨")))

            {

                agentStatusEllipse.Visibility = Visibility.Visible;

                agentStatusText.Visibility = Visibility.Visible;

                agentStatusText.Text = step.Length > 40 ? step.Substring(0, 37) + "..." : step;

                // running color

                agentStatusEllipse.Fill = new SolidColorBrush(Color.FromRgb(255, 165, 0)); // orange

                // if completed, switch to green and clear after delay

                if (step.Contains("tamamlandı", StringComparison.OrdinalIgnoreCase) || step.Contains("✅"))

                {

                    agentStatusEllipse.Fill = new SolidColorBrush(Color.FromRgb(76, 201, 176));

                    // clear after 4 seconds

                    var _ = Task.Run(async () =>

                    {

                        await Task.Delay(4000);

                        Dispatcher.Invoke(() =>

                        {

                            agentStatusEllipse.Visibility = Visibility.Collapsed;

                            agentStatusText.Visibility = Visibility.Collapsed;

                        });

                    });

                }

            }

            else if (!string.IsNullOrEmpty(step) && (step.Contains("çalışıyor", StringComparison.OrdinalIgnoreCase) || step.Contains("çalıştırılıyor", StringComparison.OrdinalIgnoreCase) || step.Contains("tamamlandı", StringComparison.OrdinalIgnoreCase) || step.Contains("✅")))

            {

                if (step.Contains("çalışıyor", StringComparison.OrdinalIgnoreCase) || step.Contains("çalıştırılıyor", StringComparison.OrdinalIgnoreCase))

                {

                    agentStatusEllipse.Visibility = Visibility.Visible;

                    agentStatusText.Visibility = Visibility.Visible;

                    agentStatusText.Text = step.Length > 40 ? step.Substring(0, 37) + "..." : step;

                    agentStatusEllipse.Fill = new SolidColorBrush(Color.FromRgb(255, 165, 0));

                }

                // if completed, switch to green and clear after delay

                if (step.Contains("tamamlandı", StringComparison.OrdinalIgnoreCase) || step.Contains("✅"))

                {

                    agentStatusEllipse.Visibility = Visibility.Visible;

                    agentStatusText.Visibility = Visibility.Visible;

                    agentStatusEllipse.Fill = new SolidColorBrush(Color.FromRgb(76, 201, 176));

                    // clear after 4 seconds

                    var _ = Task.Run(async () =>

                    {

                        await Task.Delay(4000);

                        Dispatcher.Invoke(() =>

                        {

                            agentStatusEllipse.Visibility = Visibility.Collapsed;

                            agentStatusText.Visibility = Visibility.Collapsed;

                        });

                    });

                }

            }

        }

        catch { }

    }

    private void InitializeNotificationService()

    {

        _notificationService.NotificationRaised += OnNotificationRaised;

    }

    private void InitializeToastOverlay()

    {

        toastItemsControl.ItemsSource = _toastService.Notifications;

    }

    private void InitializeAiActionService()

    {

        if (_apiClient == null || _toolExecutor == null)

            return;

        _aiActionService = new AiActionService(

            _apiClient,

            _toolExecutor,

            AddTerminalMessage,

            Notify,

            UpdateOperationStep);

    }

    private void InitializeVoiceCommandService()

    {

        try

        {

            if (_apiClient == null)

                return;

            // Create ProviderService first

            _providerService = new ProviderService(_settings, _apiClient);

            // Create VoiceCommandService

            _voiceCommandService = new VoiceCommandService(_settings);

            // Hook up events

            _voiceCommandService.RecordingStarted += VoiceCommandService_RecordingStarted;

            _voiceCommandService.RecordingStopped += VoiceCommandService_RecordingStopped;

            _voiceCommandService.TranscriptionComplete += VoiceCommandService_TranscriptionComplete;

            _voiceCommandService.TranscriptionError += VoiceCommandService_TranscriptionError;

            _voiceCommandService.VolumeChanged += VoiceCommandService_VolumeChanged;

            AddTerminalMessage(Localization.Get("🎤 Sesli Komut servisi başlatıldı", "🎤 Voice Command service started"));

        }

        catch (Exception ex)

        {

            AddTerminalMessage($"⚠️ Sesli Komut başlatma hatası: {ex.Message}");

        }

    }

    private async Task InitializeRagServicesAsync()

    {

        try

        {

            if (string.IsNullOrEmpty(_selectedFolder))

            {

                // RAG will be initialized when folder is selected

                AddTerminalMessage(Localization.Get("📁 Proje klasörü seçildiğinde RAG başlatılacak", "📁 RAG will start when a project folder is selected"));

                return;

            }

            if (!_settings.RagEnabled)

            {

                AddTerminalMessage(Localization.Get("⚪ RAG servisi ayarlandığında başlatılacak (Bkz: RAG Sekmesi)", "⚪ RAG will start after it is configured (see the RAG tab)"));

                return;

            }

            // Initialize RagService

            _ragService = new mdaiAgent.Services.RagService(_selectedFolder, _settings);

            await _ragService.InitializeAsync();

            // Initialize ProjectWatcherService

            _projectWatcherService = new ProjectWatcherService(_selectedFolder, _ragService);

            _projectWatcherService.IndexingComplete += ProjectWatcher_IndexingComplete;

            // Perform full indexing

            AddTerminalMessage(Localization.Get("📚 RAG sistemini yapılandırılıyor (tüm kod dosyaları indeksleniyor)...", "📚 Configuring RAG (indexing all code files)..."));

            var chunksIndexed = await _projectWatcherService.FullIndexAsync();

            // Start watching for changes

            _projectWatcherService.Start();

            // Pass RAG service to ChatFlowService

            _chatFlowService.UpdateRagService(_ragService);

            AddTerminalMessage($"✅ RAG başlatıldı: {chunksIndexed} kod chunk'ı indekslendi, otomatik izleme başladı");

        }

        catch (Exception ex)

        {

            AddTerminalMessage($"⚠️ RAG başlatma hatası: {ex.Message}");

        }

    }

    private void ProjectWatcher_IndexingComplete(object? sender, ProjectWatcherService.ProjectIndexingCompleteEventArgs e)

    {

        _ = Dispatcher.BeginInvoke(() =>

        {

            AddTerminalMessage($"🔄 Kodlar güncellendi: {e.ChunksIndexed} yeni chunk, {e.FilesProcessed} dosya ({e.Duration.TotalSeconds:F2}s)");

        });

    }

    private void UpdateTokenTrackerUI(int sessionTokens, decimal sessionCost, int projectTokens, decimal projectCost)

    {

        if (!Dispatcher.CheckAccess())

        {

            Dispatcher.Invoke(() => UpdateTokenTrackerUI(sessionTokens, sessionCost, projectTokens, projectCost));

            return;

        }

        try

        {

            var modelName = _apiClient?.ModelName ?? "Bilinmiyor";

            tbTokenModel.Text = modelName;

            tbTokenSession.Text = $"{sessionTokens:N0} Token (${sessionCost:F4})";

            tbTokenTotal.Text = $"{projectTokens:N0} Token (${projectCost:F4})";

            // Popup UI

            popModel.Text = modelName;

            popSessionTokens.Text = $"{sessionTokens:N0}";

            popSessionPromptTokens.Text = $"{TokenTrackerService.Instance.CurrentSessionPromptTokens:N0}";

            popSessionCompletionTokens.Text = $"{TokenTrackerService.Instance.CurrentSessionCompletionTokens:N0}";

            popSessionCost.Text = $"${sessionCost:F4}";

            popTotalTokens.Text = $"{projectTokens:N0}";

            popTotalPromptTokens.Text = $"{TokenTrackerService.Instance.TotalProjectPromptTokens:N0}";

            popTotalCompletionTokens.Text = $"{TokenTrackerService.Instance.TotalProjectCompletionTokens:N0}";

            popTotalCost.Text = $"${projectCost:F4}";

        }

        catch { }

    }

    private void TokenTrackerPanel_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)

    {

        if (tokenStatsPopup != null)

        {

            tokenStatsPopup.IsOpen = true;

        }

    }

    private void TelemetryTab_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshTelemetryPanel();
    }

    private void BottomTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (tabDiagnostics?.IsSelected == true)
            RefreshTelemetryPanel();
    }

    private void BtnExpandBottomPanel_Click(object sender, RoutedEventArgs e)
    {
        _bottomPanelExpanded = !_bottomPanelExpanded;

        if (_bottomPanelExpanded)
        {
            editorPanelRow.Height = new GridLength(0.35, GridUnitType.Star);
            bottomPanelRow.Height = new GridLength(0.65, GridUnitType.Star);
            btnExpandBottomPanel.Content = "↕";
            btnExpandBottomPanel.ToolTip = "Alt paneli küçült";
        }
        else
        {
            editorPanelRow.Height = new GridLength(1, GridUnitType.Star);
            bottomPanelRow.Height = new GridLength(230);
            btnExpandBottomPanel.Content = "↕";
            btnExpandBottomPanel.ToolTip = "Alt paneli büyüt";
        }
    }

    private void BtnDetachDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        if (_diagnosticsWindow != null)
        {
            _diagnosticsWindow.Activate();
            return;
        }

        if (tabDiagnostics == null || !bottomTabs.Items.Contains(tabDiagnostics))
            return;

        bottomTabs.SelectedItem = tabTerminal;
        bottomTabs.Items.Remove(tabDiagnostics);

        var window = new DiagnosticsWindow(tabDiagnostics)
        {
            Owner = this
        };
        _diagnosticsWindow = window;
        window.Closed += (_, _) =>
        {
            if (!bottomTabs.Items.Contains(tabDiagnostics))
                bottomTabs.Items.Add(tabDiagnostics);

            bottomTabs.SelectedItem = tabDiagnostics;
            _diagnosticsWindow = null;
        };
        window.Show();
    }

    private void BtnRefreshTelemetry_Click(object sender, RoutedEventArgs e)
    {
        RefreshTelemetryPanel();
    }

    private void BtnClearTelemetry_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            LocalizationManager.Instance.GetString("TelemetryClearConfirmation"),
            LocalizationManager.Instance.GetString("TelemetryClearTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        TokenTrackerService.Instance.ClearProjectTelemetry();
        RefreshTelemetryPanel();
        AddTerminalMessage($"🧹 {LocalizationManager.Instance.GetString("TelemetryCleared")}");
    }

    private void RefreshTelemetryPanel()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(RefreshTelemetryPanel);
            return;
        }

        try
        {
            var tracker = TokenTrackerService.Instance;
            var verification = tracker.GetVerificationTelemetry();
            var central = tracker.GetCentralTelemetrySnapshot();
            tbTelemetryRuns.Text = verification.TotalRuns.ToString("N0");
            tbTelemetrySuccess.Text = verification.SuccessfulRuns.ToString("N0");
            tbTelemetryFailed.Text = verification.FailedRuns.ToString("N0");
            tbTelemetryAverage.Text = $"{verification.AverageDurationMs:N0} ms";
            tbTelemetryLastStatus.Text = string.IsNullOrWhiteSpace(verification.LastStatus)
                ? LocalizationManager.Instance.GetString("VerificationStatusNone")
                : $"{GetLocalizedVerificationStatus(verification.LastStatus)} ({verification.LastUpdated})";
            tbTelemetryCentralSummary.Text = string.Format(
                LocalizationManager.Instance.GetString("MerkeziTelemetriOzet"),
                central.ModelSuccessRate,
                central.ProviderFailures,
                central.RetryRequests,
                central.TotalSessions);

            lstDiagnostics.ItemsSource = _fileDiagnostics
                .SelectMany(entry => entry.Value.Select(diagnostic => new DiagnosticListItem
                {
                    FilePath = entry.Key,
                    LineNumber = diagnostic.Line,
                    Message = diagnostic.Message,
                    Source = diagnostic.Source
                }))
                .OrderBy(item => item.FilePath)
                .ThenBy(item => item.LineNumber)
                .Take(200)
                .ToList();

            lstTelemetryTools.ItemsSource = tracker.GetToolTelemetrySnapshot()
                .OrderByDescending(item => item.Value.TotalCalls)
                .Select(item => new
                {
                    Name = item.Key,
                    TotalCalls = item.Value.TotalCalls,
                    SuccessRate = item.Value.TotalCalls == 0
                        ? "0%"
                        : $"{item.Value.SuccessfulCalls * 100.0 / item.Value.TotalCalls:N0}%",
                    AverageDuration = $"{item.Value.AverageDurationMs:N0} ms"
                })
                .ToList();

            lstTelemetryModels.ItemsSource = tracker.GetModelTelemetrySnapshot()
                .OrderByDescending(item => item.Value.TotalRequests)
                .Select(item => new
                {
                    Name = item.Key,
                    TotalRequests = item.Value.TotalRequests,
                    SuccessRate = item.Value.TotalRequests == 0
                        ? "0%"
                        : $"{item.Value.SuccessfulRequests * 100.0 / item.Value.TotalRequests:N0}%",
                    AverageLatency = $"{item.Value.AverageLatencyMs:N0} ms",
                    RetryRequests = item.Value.RetryRequests
                })
                .ToList();
        }
        catch (Exception ex)
        {
            AddTerminalMessage($"⚠️ Telemetri paneli güncellenemedi: {ex.Message}");
        }
    }

    private static string GetLocalizedVerificationStatus(string status)
    {
        return status switch
        {
            "All Checks Passed" => LocalizationManager.Instance.GetString("VerificationStatusAllChecksPassed"),
            _ => status
        };
    }

    private PreviewWindow GetOrOpenPreviewWindow()

    {

        if (_previewWindow == null || !_previewWindow.IsLoaded)

        {

            _previewWindow = new PreviewWindow();

            _previewWindow.Owner = this;

            if (_previewService != null)

            {

                _previewWindow.SetPreviewService(_previewService);

            }

            _previewWindow.Closed += (s, e) => _previewWindow = null;

        }

        _previewWindow.Show();

        _previewWindow.Activate();

        return _previewWindow;

    }

    private void OnPreviewUrlChanged(object? sender, string url)

    {

        if (!Dispatcher.CheckAccess())

        {

            Dispatcher.Invoke(() => OnPreviewUrlChanged(sender, url));

            return;

        }

        GetOrOpenPreviewWindow();

        AddTerminalMessage($"Önizleme: {url}");

        UpdateProcessStatus($"Önizleme: {url}");

    }

    private void OnPreviewStatusChanged(object? sender, string status)

    {

        if (!Dispatcher.CheckAccess())

        {

            Dispatcher.Invoke(() => OnPreviewStatusChanged(sender, status));

            return;

        }

        AddTerminalMessage($"Önizleme: {status}");

        UpdateProcessStatus($"Önizleme: {status}");

    }

    private void OnPreviewError(object? sender, string error)

    {

        if (!Dispatcher.CheckAccess())

        {

            Dispatcher.Invoke(() => OnPreviewError(sender, error));

            return;

        }

        AddTerminalMessage($"Önizleme hatası: {error}");

        Notify($"Önizleme hatası: {error}", NotificationSeverity.Error);

    }

    private void Notify(string message, NotificationSeverity severity = NotificationSeverity.Info)

    {

        _notificationService.Notify(message, severity);

    }

    private void OnNotificationRaised(object? sender, NotificationEventArgs e)

    {

        ShowNotification(e.Message, e.Severity);

    }

    private void ShowNotification(string message, NotificationSeverity severity = NotificationSeverity.Info)

    {

        if (!Dispatcher.CheckAccess())

        {

            _ = Dispatcher.BeginInvoke(new Action(() => ShowNotification(message, severity)));

            return;

        }

        // Sadece toast bildirimini göster

        _toastService.AddToast(message, severity);

    }

    private void ToastCloseButton_Click(object sender, RoutedEventArgs e)

    {

        if (sender is Button button && button.Tag is Guid id)

        {

            _toastService.RemoveToast(id);

        }

    }

    private void InitializeCommandPalette()

    {

        _commandPaletteItems.Clear();

        _commandPaletteItems.AddRange(new[]

        {

            new CommandPaletteItem { Label = "Dosyayı Kaydet", Description = "Aktif sekmeyi kaydeder.", ActionId = "saveCurrent" },

            new CommandPaletteItem { Label = "Tümünü Kaydet", Description = "Açık tüm dosyaları kaydeder.", ActionId = "saveAll" },

            new CommandPaletteItem { Label = "Yedekleri Görüntüle", Description = "Proje yedeklerini gösterir.", ActionId = "showBackups" },

            new CommandPaletteItem { Label = "Terminal Çıktısını Temizle", Description = "Terminal penceresini temizler.", ActionId = "clearTerminal" },

            new CommandPaletteItem { Label = "AI Refaktör Et", Description = "Aktif dosyayı AI ile yeniden düzenler.", ActionId = "aiRefactor" },

            new CommandPaletteItem { Label = "AI Yorumları Geliştir", Description = "Aktif dosyaya daha iyi yorumlar ekler.", ActionId = "aiComments" },

            new CommandPaletteItem { Label = "AI Dosyayı Açıkla", Description = "Aktif dosyanın ne yaptığını açıklar.", ActionId = "aiExplain" },

            new CommandPaletteItem { Label = "AI Test Üret", Description = "Aktif dosya için test senaryosu oluşturur.", ActionId = "aiGenerateTests" },

            new CommandPaletteItem { Label = "AI Plan Modu", Description = "Projeye uygun bir AI yol haritası hazırlar.", ActionId = "aiPlanMode" },

            new CommandPaletteItem { Label = "Son AI Yanıtını Uygula", Description = "AI'den gelen son yanıtı aktif dosyaya uygular.", ActionId = "applyLastAiResponse" },

            new CommandPaletteItem { Label = "Dokümanı Biçimlendir", Description = "Aktif dosyada basit bir biçimlendirme uygular.", ActionId = "formatDocument" },

            new CommandPaletteItem { Label = "Hızlı Dosya Aç", Description = "Proje içinden hızlı dosya açar.", ActionId = "quickOpenFile" }

        });

        _filteredPaletteItems.Clear();

        _filteredPaletteItems.AddRange(_commandPaletteItems);

        lstCommandPalette.ItemsSource = _filteredPaletteItems;

    }

    private void InitializeTerminalService()

    {

        _terminalService = new TerminalService(() => _selectedFolder);

        _terminalService.TerminalTextChanged += OnTerminalTextChanged;

        _terminalService.StatusChanged += OnTerminalStatusChanged;

        _terminalService.IsBusyChanged += OnTerminalIsBusyChanged;

        tbTerminalStatus.Text = string.Empty;

    }

    private void OnTerminalTextChanged(object? sender, string terminalText)

    {

        if (!Dispatcher.CheckAccess())

        {

            _ = Dispatcher.BeginInvoke(new Action(() => OnTerminalTextChanged(sender, terminalText)));

            return;

        }

        txtTerminal.Text = terminalText;

        txtTerminal.CaretIndex = txtTerminal.Text.Length;

        txtTerminal.ScrollToEnd();

    }

    private void OnTerminalStatusChanged(object? sender, string status)

    {

        if (!Dispatcher.CheckAccess())

        {

            _ = Dispatcher.BeginInvoke(new Action(() => OnTerminalStatusChanged(sender, status)));

            return;

        }

        tbTerminalStatus.Text = status;

    }

    private void OnTerminalIsBusyChanged(object? sender, bool isBusy)

    {

        if (!Dispatcher.CheckAccess())

        {

            _ = Dispatcher.BeginInvoke(new Action(() => OnTerminalIsBusyChanged(sender, isBusy)));

            return;

        }

        btnRunCommand.IsEnabled = !isBusy;

        btnKillProcess.IsEnabled = isBusy;

        // Üst paneldeki Run/Stop butonları

        btnRunApp.IsEnabled = !isBusy;

        btnRunApp.Foreground = !isBusy ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.LimeGreen) : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray);

        btnStopApp.IsEnabled = isBusy;

        btnStopApp.Foreground = isBusy ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Tomato) : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray);

        btnHotReload.IsEnabled = isBusy;

        btnHotReload.Foreground = isBusy ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Yellow) : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray);

        if (!isBusy)

        {

            txtTerminalInput.Focus();

        }

    }

    private void BtnCommandPalette_Click(object sender, RoutedEventArgs e)

    {

        ShowCommandPalette();

    }

    private void ShowCommandPalette()

    {

        paletteOverlay.Visibility = Visibility.Visible;

        txtCommandPalette.Text = string.Empty;

        txtCommandPalette.Focus();

        FilterCommandPalette(string.Empty);

    }

    private void HideCommandPalette()

    {

        paletteOverlay.Visibility = Visibility.Collapsed;

    }

    private void FilterCommandPalette(string query)

    {

        _filteredPaletteItems.Clear();

        var filtered = string.IsNullOrWhiteSpace(query)

            ? _commandPaletteItems

            : _commandPaletteItems.Where(item => item.Label.Contains(query, StringComparison.OrdinalIgnoreCase)

                || item.Description.Contains(query, StringComparison.OrdinalIgnoreCase));

        _filteredPaletteItems.AddRange(filtered);

        lstCommandPalette.ItemsSource = null;

        lstCommandPalette.ItemsSource = _filteredPaletteItems;

        if (_filteredPaletteItems.Count > 0)

        {

            lstCommandPalette.SelectedIndex = 0;

        }

    }

    private void TxtCommandPalette_TextChanged(object sender, TextChangedEventArgs e)

    {

        if (sender is TextBox tb)

        {

            FilterCommandPalette(tb.Text.Trim());

        }

    }

    private void TxtCommandPalette_KeyDown(object sender, KeyEventArgs e)

    {

        if (e.Key == Key.Escape)

        {

            HideCommandPalette();

            e.Handled = true;

            return;

        }

        if (e.Key == Key.Enter && lstCommandPalette.SelectedItem is CommandPaletteItem selected)

        {

            ExecuteCommandPaletteItem(selected);

            e.Handled = true;

        }

        else if (e.Key == Key.Down)

        {

            if (lstCommandPalette.Items.Count > 0)

            {

                lstCommandPalette.SelectedIndex = Math.Min(lstCommandPalette.Items.Count - 1, lstCommandPalette.SelectedIndex + 1);

                lstCommandPalette.ScrollIntoView(lstCommandPalette.SelectedItem);

            }

            e.Handled = true;

        }

        else if (e.Key == Key.Up)

        {

            if (lstCommandPalette.Items.Count > 0)

            {

                lstCommandPalette.SelectedIndex = Math.Max(0, lstCommandPalette.SelectedIndex - 1);

                lstCommandPalette.ScrollIntoView(lstCommandPalette.SelectedItem);

            }

            e.Handled = true;

        }

    }

    private void LstCommandPalette_MouseDoubleClick(object sender, MouseButtonEventArgs e)

    {

        if (lstCommandPalette.SelectedItem is CommandPaletteItem selected)

        {

            ExecuteCommandPaletteItem(selected);

        }

    }

    private void ExecuteCommandPaletteItem(CommandPaletteItem item)

    {

        HideCommandPalette();

        switch (item.ActionId)

        {

            case "saveCurrent":

                SaveCurrentFile();

                break;

            case "saveAll":

                SaveAllTabs();

                break;

            case "showBackups":

                BtnShowBackups_Click(null!, null!);

                break;

            case "clearTerminal":

                BtnClearTerminal_Click(null!, null!);

                break;

            case "aiRefactor":

                MenuItem_AiRefactorCurrentFile_Click(null!, null!);

                break;

            case "aiComments":

                MenuItem_AiAddCommentsToCurrentFile_Click(null!, null!);

                break;

            case "aiExplain":

                MenuItem_AiExplainCurrentFile_Click(null!, null!);

                break;

            case "aiGenerateTests":

                MenuItem_AiGenerateTestsForCurrentFile_Click(null!, null!);

                break;

            case "aiPlanMode":

                MenuItem_AiPlanMode_Click(null!, null!);

                break;

            case "applyLastAiResponse":

                MenuItem_ApplyLastAiResponseToFile_Click(null!, null!);

                break;

            case "formatDocument":

                BtnFormatDocument_Click(null!, null!);

                break;

            case "quickOpenFile":

                BtnQuickOpenFile_Click(null!, null!);

                break;

            default:

                AddTerminalMessage($"Bilinmeyen komut: {item.Label}");

                break;

        }

    }

    private void BtnAiActions_Click(object sender, RoutedEventArgs e)

    {

        if (btnAiActions.ContextMenu != null)

        {

            btnAiActions.ContextMenu.PlacementTarget = btnAiActions;

            btnAiActions.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;

            btnAiActions.ContextMenu.IsOpen = true;

        }

    }

    private void ToggleSafeAutomation_Click(object sender, RoutedEventArgs e)

    {

        _safeAutomationEnabled = !_safeAutomationEnabled;

        _toolExecutor = new ToolExecutor(

            _selectedFolder,

            AddTerminalMessage,

            async (message) => await RunOnUiThreadAsync(() => ShowConfirmCommandWindowAsync(message)),

            ShowDiffWindowConfirmAsync,

            _safeAutomationEnabled,

            UpdateOperationStep,

            Notify,

            confirmUiInvoker: async operation => await RunOnUiThreadAsync(operation),

            fileChangeUiInvoker: async operation => await RunOnUiThreadAsync(operation),

            askUserOptionsWithSelection: async (question, options, allowMultiple) => await RunOnUiThreadAsync(() => ShowAskUserDialogAsync(question, options, allowMultiple)),

            requestExternalFolderAccess: async folderPath => await RunOnUiThreadAsync(() => ShowExternalFolderAccessAsync(folderPath))

        );

        if (_safeAutomationEnabled)

        {

            btnSafeAutomation.Background = new SolidColorBrush(Color.FromRgb(76, 201, 176));

            btnSafeAutomation.Foreground = new SolidColorBrush(Color.FromRgb(30, 30, 30));

            tbTerminalStatus.Text = LocalizationManager.Instance.GetString("GuvenliModACIK");

            UpdateProcessStatus(Localization.Get("Güvenli mod etkinleştirildi", "Safe mode enabled"));

        }

        else

        {

            btnSafeAutomation.Background = new SolidColorBrush(Color.FromRgb(45, 45, 48));

            btnSafeAutomation.Foreground = new SolidColorBrush(Color.FromRgb(212, 212, 212));

            tbTerminalStatus.Text = LocalizationManager.Instance.GetString("GuvenliModKAPAL");

            UpdateProcessStatus(Localization.Get("Güvenli mod devre dışı bırakıldı", "Safe mode disabled"));

        }

    }

    private void UpdateProcessStatus(string message)

    {

        if (lstProcessStatus != null)

        {

            var timestamp = DateTime.Now.ToString("HH:mm:ss");

            var statusItem = $"[{timestamp}] {message}";

            if (lstProcessStatus.Items.Count > 0)

            {

                var items = lstProcessStatus.Items.Cast<string>().ToList();

                items.Insert(0, statusItem);

                if (items.Count > 10) items.RemoveAt(items.Count - 1);

                lstProcessStatus.Items.Clear();

                foreach (var item in items)

                    lstProcessStatus.Items.Add(item);

            }

            else

            {

                lstProcessStatus.Items.Add(statusItem);

            }

            tbProcessStatus.Text = message;

        }

    }

    private void MenuItem_AiRefactorCurrentFile_Click(object sender, RoutedEventArgs e)

    {

        RunBackground(RunAiActionOnCurrentFileAsync(

            "Dosyayı Refaktör Et",

            "Lütfen açık dosyanın içeriğini al ve kodu daha okunabilir, daha iyi yapılandırılmış ve mevcut işlevselliği koruyacak şekilde refaktör et. Yanıtı yalnızca güncellenmiş dosya içeriği olarak kod bloğunda ver.",

            updateFile: true

        ), "AiRefactorCurrentFile");

    }

    private void MenuItem_AiAddCommentsToCurrentFile_Click(object sender, RoutedEventArgs e)

    {

        RunBackground(RunAiActionOnCurrentFileAsync(

            "Yorumları Geliştir",

            "Lütfen açık dosyayı al ve kodu anlatan anlamlı satır içi yorumlar ekle. Yanıtı yalnızca güncellenmiş dosya içeriği olarak kod bloğunda ver.",

            updateFile: true

        ), "AiAddComments");

    }

    private void MenuItem_AiExplainCurrentFile_Click(object sender, RoutedEventArgs e)

    {

        RunBackground(RunAiActionOnCurrentFileAsync(

            "Dosyayı Açıkla",

            "Lütfen açık dosyanın ne yaptığını ve önemli kısımlarını net ve kısa bir şekilde açıkla.",

            updateFile: false

        ), "AiExplain");

    }

    private void MenuItem_AiGenerateTestsForCurrentFile_Click(object sender, RoutedEventArgs e)

    {

        if (tcEditor.SelectedItem is TabItem activeTab && activeTab.Tag is TabInfo tabInfo)

        {

            var testFilePath = GetTestFilePath(tabInfo.FilePath);

            RunBackground(RunAiActionOnCurrentFileAsync(

                "Test Senaryosu Üret",

                $"Lütfen şu dosya için uygun bir test dosyası oluştur: {Path.GetFileName(tabInfo.FilePath)}. Sadece test dosyası içeriğini ver ve dosya yolunu belirtme.",

                updateFile: true,

                targetFilePath: testFilePath

            ), "AiGenerateTests");

        }

    }

    private void MenuItem_AiPlanMode_Click(object sender, RoutedEventArgs e)

    {

        RunBackground(RunAiPlanModeAsync(), "AiPlanMode");

    }

    private async Task RunAiPlanModeAsync()

    {

        if (string.IsNullOrEmpty(_selectedFolder))

        {

            MessageBox.Show(LocalizationManager.Instance.GetString("PlanModuNuKullanabilmekIcinLutfenSolTaraftanBirProjeKlasoruSecin"), LocalizationManager.Instance.GetString("ProjeSecilmedi"), MessageBoxButton.OK, MessageBoxImage.Warning);

            return;

        }

        if (_apiClient == null)

        {

            MessageBox.Show(LocalizationManager.Instance.GetString("LutfenAyarlardanAPIAnahtariniziEkleyin"), LocalizationManager.Instance.GetString("AIHazirlanmadi"), MessageBoxButton.OK, MessageBoxImage.Warning);

            return;

        }

        var dialog = new MessageDialog(

            $"{LocalizationManager.Instance.GetString("PlanModuProjeniziInceleyerekBirGelistirmeYolHaritasiMimariOnerilerEklenecekOzelliklerVsCikarir")}\n\n" +

            $"{LocalizationManager.Instance.GetString("BuIslemProjenizinBuyukluguneGoreBirazZamanAlabilir")}\n" +

            LocalizationManager.Instance.GetString("DevamEtmekVeBirGelistirmePlaniOlusturmakIstiyorMusunuz"),

            LocalizationManager.Instance.GetString("PlanModuNuBaslat"));

        dialog.Owner = this;

        if (dialog.ShowDialog() != true)

        {

            return;

        }

        _isPlanModeRunning = true;

        btnPlanMode.IsEnabled = false;

        UpdateOperationStep("Plan çıkarılıyor...");

        AddTerminalMessage(LocalizationManager.Instance.GetString("PlanModuIcinProjeAnalizEdiliyorLutfenBekleyin"));

        var selectedFilePath = tcEditor.SelectedItem is TabItem activeTab && activeTab.Tag is TabInfo tabInfo ? tabInfo.FilePath : string.Empty;

        var currentFileContent = tcEditor.SelectedItem is TabItem actTab && actTab.Content is TextEditor editor ? editor.Text : string.Empty;

        try

        {

            var planWindow = new PlanWindow(_planModeService, selectedFilePath, currentFileContent, _selectedFolder, _settings.SystemPrompt, _cachedPlanResult)

            {

                Owner = this

            };

            if (planWindow.ShowDialog() == true && !string.IsNullOrEmpty(planWindow.SelectedOptionId))

            {

                var planResult = planWindow.PlanResult;

                if (planResult == null || !planResult.Success) return;

                // Üretilen planı cache'e al — bir sonraki açılışta tekrar üretilmez

                _cachedPlanResult = planResult;

                var selectedOption = planResult.Options.FirstOrDefault(opt => opt.Id == planWindow.SelectedOptionId);

                if (selectedOption != null)

                {

                    var followUpResult = await _planModeService.CreateFollowUpAsync(selectedOption, selectedFilePath, currentFileContent, _selectedFolder, _settings.SystemPrompt);

                    if (followUpResult.Success)

                    {

                        foreach (var chatMessage in followUpResult.Messages)

                        {

                            AddChatMessage(chatMessage.Sender, chatMessage.Content);

                        }

                        if (_chatPanelViewModel != null)

                        {

                            _chatPanelViewModel.TodoItems.Clear();

                            var todoItems = PlanModeHelper.ParseTodoItemsFromPlanText(followUpResult.FullPlanText);

                            foreach (var todo in todoItems)

                            {

                                _chatPanelViewModel.TodoItems.Add(todo);

                            }

                            _chatPanelViewModel.TodoPanelVisible = true;

                            _chatPanelViewModel.TimelinePanelVisible = true;

                            _chatPanelViewModel.FileChangesPanelVisible = true;

                            if (_chatPanelViewModel.TodoItems.Count > 0)

                            {

                                _chatPanelViewModel.TodoItems[0].Status = TodoTaskStatus.Running;

                            }

                            if (timelinePanel != null) timelinePanel.Visibility = Visibility.Collapsed;

                            if (todoPanel != null) todoPanel.Visibility = Visibility.Visible;

                            if (fileChangesPanel != null) fileChangesPanel.Visibility = Visibility.Collapsed;

                            if (chatHistoryPanel != null) chatHistoryPanel.Visibility = Visibility.Collapsed;

                            if (btnShowTodoList != null)

                            {

                                btnShowTodoList.Background = TryFindResource("SurfaceAlt2Brush") as Brush ?? new SolidColorBrush(Color.FromRgb(60, 60, 80));

                                btnShowTodoList.Foreground = TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White;

                            }

                            if (btnShowTimeline != null)

                            {

                                btnShowTimeline.Background = Brushes.Transparent;

                                btnShowTimeline.Foreground = TryFindResource("TextSecondaryBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(180, 180, 180));

                            }

                            if (btnShowFileChanges != null)

                            {

                                btnShowFileChanges.Background = Brushes.Transparent;

                                btnShowFileChanges.Foreground = TryFindResource("TextSecondaryBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(180, 180, 180));

                            }

                            if (btnShowChatHistory != null)

                            {

                                btnShowChatHistory.Background = Brushes.Transparent;

                                btnShowChatHistory.Foreground = TryFindResource("TextSecondaryBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(180, 180, 180));

                            }

                        }

                        var count = _chatPanelViewModel?.TodoItems.Count ?? 0;

                        MessageBox.Show(LocalizationManager.Instance.GetString("PlanSecimiSohbeteEklendiVeCountAdimlikTODOListesineAktarildiAINinOnerdigiAdimlariTODOPanelindenTakipEdebilirsiniz").Replace("{count}", count.ToString()), "Plan Modu", MessageBoxButton.OK, MessageBoxImage.Information);

                    }

                    else

                    {

                        MessageBox.Show(followUpResult.ErrorMessage, "Plan Modu", MessageBoxButton.OK, MessageBoxImage.Warning);

                    }

                }

            }

        }

        catch (Exception ex)

        {

            MessageBox.Show(LocalizationManager.Instance.GetString("PlanModuSirasindaHataOlustuExMessage").Replace("{ex.Message}", ex.Message), "Plan Modu", MessageBoxButton.OK, MessageBoxImage.Error);

        }

        finally

        {

            _isPlanModeRunning = false;

            btnPlanMode.IsEnabled = true;

            UpdateOperationStep("Hazır");

        }

    }

    private string GetTestFilePath(string currentFilePath)

    {

        var directory = Path.GetDirectoryName(currentFilePath) ?? string.Empty;

        var baseName = Path.GetFileNameWithoutExtension(currentFilePath);

        var extension = Path.GetExtension(currentFilePath).ToLowerInvariant();

        var testFileName = extension switch

        {

            ".cs" => $"{baseName}Tests.cs",

            ".py" => $"{baseName}_test.py",

            ".js" => $"{baseName}.test.js",

            ".ts" => $"{baseName}.test.ts",

            ".java" => $"{baseName}Test.java",

            _ => $"{baseName}_tests{extension}"

        };

        return Path.Combine(directory, testFileName);

    }

    private async Task<AiActionResult?> RunAiActionOnCurrentFileAsync(string actionTitle, string userRequest, bool updateFile, string? targetFilePath = null)

    {

        if (_apiClient == null || _toolExecutor == null)

        {

            MessageBox.Show(LocalizationManager.Instance.GetString("LutfenOnceAPIAnahtariniAyarlardanEkleyin"), LocalizationManager.Instance.GetString("AIHazirlanmadi"), MessageBoxButton.OK, MessageBoxImage.Warning);

            return null;

        }

        if (!(tcEditor.SelectedItem is TabItem activeTab && activeTab.Tag is TabInfo tabInfo && activeTab.Content is TextEditor activeEditor))

        {

            MessageBox.Show(LocalizationManager.Instance.GetString("LutfenOnceBirDosyaAcin"), LocalizationManager.Instance.GetString("DosyaBulunamadi"), MessageBoxButton.OK, MessageBoxImage.Information);

            return null;

        }

        var filePath = tabInfo.FilePath;

        var finalTargetPath = targetFilePath ?? filePath;

        var fileContent = activeEditor.Text;

        if (_aiActionService == null)

        {

            AddTerminalMessage(LocalizationManager.Instance.GetString("AIEylemServisiHazirDegil"));

            Notify("AI eylem servisi kullanılamıyor.", NotificationSeverity.Error);

            return null;

        }

        var request = new AiActionRequest

        {

            ActionTitle = actionTitle,

            UserRequest = userRequest,

            FilePath = filePath,

            FileContent = fileContent,

            UpdateFile = updateFile,

            TargetFilePath = finalTargetPath,

            SelectedFolder = _selectedFolder ?? string.Empty,

            SystemPrompt = _settings?.SystemPrompt ?? string.Empty

        };

        var result = await _aiActionService.RunFileActionAsync(request);

        if (!result.Success)

        {

            AddTerminalMessage($"AI eylemi sırasında hata oluştu: {result.Message}");

            Notify($"AI eylemi sırasında hata oluştu: {result.Message}", NotificationSeverity.Error);

            if (!updateFile && !string.IsNullOrEmpty(result.AssistantContent))

            {

                MessageBox.Show(result.AssistantContent, actionTitle, MessageBoxButton.OK, MessageBoxImage.Information);

            }

            return result;

        }

        if (result.HasToolCalls && result.ToolCallFailure)

        {

            AddTerminalMessage($"AI araç çağrılarının bazıları başarısız oldu.");

            Notify($"AI araç çağrılarının bazıları başarısız oldu.", NotificationSeverity.Warning);

            return result;

        }

        if (result.RequiresConfirmation && !string.IsNullOrWhiteSpace(result.ProposedContent))

        {

            var shouldApply = await ShowDiffWindowConfirmAsync(result.ProposedFilePath, result.OriginalContent, result.ProposedContent);

            if (!shouldApply)

            {

                AddTerminalMessage(LocalizationManager.Instance.GetString("AIOnerisiKullaniciTarafindanReddedildi"));

                Notify("AI önerisi reddedildi.", NotificationSeverity.Info);

                return result;

            }

            var applied = await ApplyAiSuggestedContentAsync(result.ProposedFilePath, result.ProposedContent);

            if (!applied)

            {

                AddTerminalMessage(LocalizationManager.Instance.GetString("AIOnerisininUygulanmasiBasarisizOldu"));

                Notify("AI önerisi uygulanamadı.", NotificationSeverity.Error);

                return result;

            }

            result.UpdatedFile = true;

            result.UpdatedFilePaths = new List<string> { result.ProposedFilePath };

        }

        if (result.UpdatedFile)

        {

            ReloadTabIfOpen(finalTargetPath);

            if (!string.IsNullOrEmpty(_selectedFolder))

            {

                LoadFileTree(_selectedFolder);

            }

        }

        else if (result.UpdatedFilePaths.Count > 0)

        {

            ReloadTabIfOpen(finalTargetPath);

            if (!string.IsNullOrEmpty(_selectedFolder))

            {

                LoadFileTree(_selectedFolder);

            }

        }

        if (!updateFile && !string.IsNullOrEmpty(result.AssistantContent))

        {

            MessageBox.Show(result.AssistantContent, actionTitle, MessageBoxButton.OK, MessageBoxImage.Information);

        }

        return result;

    }

    private string ExtractCodeFromAiResponse(string content)

    {

        var codeBlockPattern = "```(?:[a-zA-Z0-9_+-]*)\\r?\\n(?<code>[\\s\\S]*?)```";

        var match = Regex.Match(content, codeBlockPattern);

        if (match.Success)

        {

            return match.Groups["code"].Value.Trim();

        }

        return content.Trim();

    }

    private async Task<bool> ApplyAiSuggestedContentAsync(string filePath, string content)

    {

        if (!Dispatcher.CheckAccess())

        {

            var operation = Dispatcher.InvokeAsync(async () => await ApplyAiSuggestedContentAsync(filePath, content));

            return await operation.Task.Unwrap();

        }

        try

        {

            var directory = Path.GetDirectoryName(filePath);

            if (!string.IsNullOrWhiteSpace(directory))

            {

                Directory.CreateDirectory(directory);

            }

            string? backupPath = null;

            if (File.Exists(filePath))

            {

                backupPath = CreateBackupForFile(filePath);

            }

            var tempPath = Path.Combine(directory ?? Path.GetTempPath(), $"~{Path.GetFileName(filePath)}.tmp");

            await File.WriteAllTextAsync(tempPath, content);

            if (File.Exists(filePath))

            {

                File.Copy(tempPath, filePath, true);

                File.Delete(tempPath);

            }

            else

            {

                File.Move(tempPath, filePath);

            }

            AddTerminalMessage($"AI önerisi güvenli şekilde yazıldı: {filePath}");

            return true;

        }

        catch (Exception ex)

        {

            AddTerminalMessage($"AI önerisi yazılırken hata oluştu: {ex.Message}");

            try

            {

                var directory = Path.GetDirectoryName(filePath);

                if (!string.IsNullOrWhiteSpace(directory))

                {

                    var tempPath = Path.Combine(directory, $"~{Path.GetFileName(filePath)}.tmp");

                    if (File.Exists(tempPath))

                        File.Delete(tempPath);

                }

            }

            catch { }

            return false;

        }

    }

    private string CreateBackupForFile(string filePath)

    {

        var backupDir = !string.IsNullOrEmpty(_selectedFolder) && Directory.Exists(_selectedFolder)

            ? Path.Combine(_selectedFolder, ".mdai", "backup")

            : Path.Combine(Path.GetDirectoryName(filePath) ?? string.Empty, ".mdai", "backup");

        if (!Directory.Exists(backupDir))

        {

            Directory.CreateDirectory(backupDir);

        }

        var relativeDir = !string.IsNullOrEmpty(_selectedFolder) && Directory.Exists(_selectedFolder)

            ? Path.GetDirectoryName(Path.GetRelativePath(_selectedFolder, filePath)) ?? string.Empty

            : string.Empty;

        if (!string.IsNullOrEmpty(relativeDir) && relativeDir != ".")

        {

            backupDir = Path.Combine(backupDir, relativeDir);

            if (!Directory.Exists(backupDir))

            {

                Directory.CreateDirectory(backupDir);

            }

        }

        var backupFileName = $"{Path.GetFileNameWithoutExtension(filePath)}_{DateTime.Now:yyyyMMdd_HHmmss}{Path.GetExtension(filePath)}";

        var backupPath = Path.Combine(backupDir, backupFileName);

        File.Copy(filePath, backupPath, true);

        AddTerminalMessage($"AI önerisi için yedek alındı: {backupPath}");

        return backupPath;

    }

    private void BtnShowBackups_Click(object sender, RoutedEventArgs e)

    {

        if (string.IsNullOrEmpty(_selectedFolder) || !Directory.Exists(_selectedFolder))

        {

            MessageBox.Show(LocalizationManager.Instance.GetString("LutfenOnceBirProjeKlasoruSecin"), LocalizationManager.Instance.GetString("YedekleriGoruntule"), MessageBoxButton.OK, MessageBoxImage.Warning);

            return;

        }

        var backupWindow = new BackupViewerWindow(_selectedFolder)

        {

            Owner = this

        };

        backupWindow.ShowDialog();

    }

    private void BtnOpenTerminalPath_Click(object sender, RoutedEventArgs e)

    {

        var terminalText = txtTerminal.Text;

        if (string.IsNullOrWhiteSpace(terminalText))

        {

            AddTerminalMessage(LocalizationManager.Instance.GetString("TerminaldeDosyaYoluBulunamadi"));

            return;

        }

        var lines = terminalText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        var lastLine = lines.Reverse().FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));

        if (lastLine == null)

        {

            AddTerminalMessage(LocalizationManager.Instance.GetString("TerminaldeUygunBirSatirBulunamadi"));

            return;

        }

        var path = ExtractPathFromText(lastLine);

        if (string.IsNullOrEmpty(path) || !File.Exists(path))

        {

            MessageBox.Show(LocalizationManager.Instance.GetString("TerminalMetnindenGecerliBirDosyaYoluBulunamadi"), LocalizationManager.Instance.GetString("DosyaAcma"), MessageBoxButton.OK, MessageBoxImage.Information);

            return;

        }

        OpenFile(path);

    }

    private void BtnAbout_Click(object sender, RoutedEventArgs e)

    {

        var aboutWindow = new AboutWindow

        {

            Owner = this

        };

        aboutWindow.ShowDialog();

    }

    private void BtnPluginManager_Click(object sender, RoutedEventArgs e)

    {
        try
        {
            var pluginWindow = new PluginManagerWindow();
            if (this.IsLoaded && this.IsVisible)
            {
                pluginWindow.Owner = this;
            }
            pluginWindow.ShowDialog();
        }

        catch (Exception ex)

        {

            MessageBox.Show(LocalizationManager.Instance.GetString("EklentiYoneticisiAcilamadiExMessage").Replace("{ex.Message}", ex.Message), LocalizationManager.Instance.GetString("Hata"), MessageBoxButton.OK, MessageBoxImage.Error);

        }

    }

    // ── Split Editor (Yan Yana Editör) ────────────────────────────────
    private void BtnSplitEditor_Click(object sender, RoutedEventArgs e)
    {
        _isSplitEditorActive = !_isSplitEditorActive;

        if (_isSplitEditorActive)
        {
            colEditorLeft.Width = new GridLength(1, GridUnitType.Star);
            colEditorRight.Width = new GridLength(1, GridUnitType.Star);
            splitterEditor.Visibility = Visibility.Visible;
            tcEditorRight.Visibility = Visibility.Visible;

            if (tcEditor.Items.Count > 1)
            {
                var itemsToMove = tcEditor.Items.Cast<TabItem>().Skip(1).ToList();
                foreach (var tab in itemsToMove)
                {
                    tcEditor.Items.Remove(tab);
                    tcEditorRight.Items.Add(tab);
                }
                tcEditorRight.SelectedIndex = 0;
            }
            else if (tcEditor.SelectedItem is TabItem activeTab && activeTab.Tag is TabInfo activeTabInfo)
            {
                var duplicateTab = CreateTabItem(activeTabInfo.FilePath);
                tcEditorRight.Items.Add(duplicateTab);
                tcEditorRight.SelectedIndex = 0;
            }

            btnSplitEditor.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1a365d"));
            btnSplitEditor.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563eb"));
        }
        else
        {
            var itemsToMove = tcEditorRight.Items.Cast<TabItem>().ToList();
            tcEditorRight.Items.Clear();

            foreach (var tab in itemsToMove)
            {
                if (tab.Tag is TabInfo tabInfo && !tcEditor.Items.Cast<TabItem>().Any(t => t.Tag is TabInfo ti && string.Equals(ti.FilePath, tabInfo.FilePath, StringComparison.OrdinalIgnoreCase)))
                {
                    tcEditor.Items.Add(tab);
                }
            }

            colEditorRight.Width = new GridLength(0);
            splitterEditor.Visibility = Visibility.Collapsed;
            tcEditorRight.Visibility = Visibility.Collapsed;

            btnSplitEditor.ClearValue(Button.BackgroundProperty);
            btnSplitEditor.ClearValue(Button.BorderBrushProperty);
        }
    }

    private void TcEditorRight_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source == tcEditorRight)
        {
            if (tcEditorRight.SelectedItem is TabItem activeTab && activeTab.Tag is TabInfo tabInfo)
            {
                _currentOpenFile = tabInfo.FilePath;
                UpdateBreadcrumb(_currentOpenFile);
                UpdateLspStatusBar(_currentOpenFile);
            }
        }
    }

    // ── LSP Durum Badge ──────────────────────────────────────────────
    private void LspStatusBadge_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        BtnPluginManager_Click(sender, new RoutedEventArgs());
    }

    /// <summary>
    /// Aktif dosya uzantısına göre toolbar LSP badge'ini günceller.
    /// 🟢 Aktif | 🔵 Hazır | 🟡 Kurulu | 🔴 Hata | ⚫ Kurulu değil | 🟩 Plugin
    /// </summary>
    private void UpdateLspStatusBar(string? filePath)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => UpdateLspStatusBar(filePath)); return; }

        if (string.IsNullOrEmpty(filePath))
        {
            lspStatusDot.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x77));
            txtLspStatus.Text = "LSP";
            txtLspStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x88, 0x99, 0xaa));
            lspStatusBadge.ToolTip = LocalizationManager.Instance.GetString("LspNoOpenFile");
            return;
        }

        var ext = System.IO.Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        bool isActivelyServing = _languageServerServices.TryGetValue(ext, out var lsvc) && lsvc.IsServingLanguage(ext);

        var serverInfo = PluginManager.LspServerManager.GetAllLspServers()
            .FirstOrDefault(s => s.FileExtensions.Any(fe =>
                fe.TrimStart('.').Equals(ext, StringComparison.OrdinalIgnoreCase)));

        string dotColor, textColor, label, detail;

        if (serverInfo == null)
        {
            dotColor = "#226644"; textColor = "#55aa77";
            label = ext.ToUpperInvariant() + " · Plugin";
            detail = LocalizationManager.Instance.GetString("LspNoExternalServerForExt").Replace("{ext}", ext);
        }
        else if (isActivelyServing)
        {
            dotColor = "#22cc77"; textColor = "#88ffbb";
            label = serverInfo.Name.Split(' ')[0] + " LSP · " + LocalizationManager.Instance.GetString("LspStatusActive");
            detail = LocalizationManager.Instance.GetString("LspStatusActiveDetail")
                .Replace("{serverName}", serverInfo.Name)
                .Replace("{path}", serverInfo.ResolvedExecutablePath ?? LocalizationManager.Instance.GetString("LspUnknownPath"));
        }
        else if (!string.IsNullOrEmpty(serverInfo.LastError))
        {
            dotColor = "#ee4444"; textColor = "#ff8888";
            label = serverInfo.Name.Split(' ')[0] + " LSP · " + LocalizationManager.Instance.GetString("LspStatusError");
            detail = LocalizationManager.Instance.GetString("LspStatusErrorDetail").Replace("{error}", serverInfo.LastError);
        }
        else if (serverInfo.IsReady)
        {
            dotColor = "#4488ff"; textColor = "#88bbff";
            label = serverInfo.Name.Split(' ')[0] + " LSP · " + LocalizationManager.Instance.GetString("LspStatusReady");
            detail = LocalizationManager.Instance.GetString("LspStatusReadyDetail")
                .Replace("{serverName}", serverInfo.Name)
                .Replace("{path}", serverInfo.ResolvedExecutablePath ?? LocalizationManager.Instance.GetString("LspUnknownPath"));
        }
        else if (serverInfo.IsInstalled)
        {
            dotColor = "#ddaa00"; textColor = "#ffcc44";
            label = serverInfo.Name.Split(' ')[0] + " LSP · " + LocalizationManager.Instance.GetString("LspStatusInstalled");
            detail = LocalizationManager.Instance.GetString("LspStatusInstalledDetail").Replace("{serverName}", serverInfo.Name);
        }
        else
        {
            dotColor = "#555577"; textColor = "#8899aa";
            label = serverInfo.Name.Split(' ')[0] + " LSP · " + LocalizationManager.Instance.GetString("LspStatusNotInstalled");
            detail = LocalizationManager.Instance.GetString("LspStatusNotInstalledDetail").Replace("{serverName}", serverInfo.Name);
        }

        var toColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(dotColor);
        var txColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(textColor);
        lspStatusDot.Fill = new System.Windows.Media.SolidColorBrush(toColor);
        txtLspStatus.Text = label;
        txtLspStatus.Foreground = new System.Windows.Media.SolidColorBrush(txColor);
        lspStatusBadge.ToolTip = detail;
    }

    private void BtnFormatDocument_Click(object sender, RoutedEventArgs e)

    {

        if (!(tcEditor.SelectedItem is TabItem activeTab && activeTab.Content is TextEditor editor))

        {

            AddTerminalMessage(LocalizationManager.Instance.GetString("FormatlamaIcinAktifBirDosyaSekmesiSecin"));

            return;

        }

        var originalText = editor.Text;

        var lines = originalText

            .Replace("\r\n", "\n")

            .Replace("\r", "\n")

            .Split('\n');

        for (var i = 0; i < lines.Length; i++)

        {

            lines[i] = Regex.Replace(lines[i].TrimEnd(), "\t", "    ");

        }

        var formatted = string.Join("\r\n", lines);

        formatted = Regex.Replace(formatted, "[ \t]+\r\n", "\r\n");

        formatted = Regex.Replace(formatted, "(\r\n){3,}", "\r\n\r\n");

        if (formatted != originalText)

        {

            editor.Text = formatted;

            AddTerminalMessage(LocalizationManager.Instance.GetString("DokumanBicimlendirildi"));

        }

        else

        {

            AddTerminalMessage(LocalizationManager.Instance.GetString("DokumanZatenDuzgunFormatlanmis"));

        }

    }

    private void BtnQuickOpenFile_Click(object sender, RoutedEventArgs e)

    {

        var initialDir = !string.IsNullOrEmpty(_selectedFolder) && Directory.Exists(_selectedFolder)

            ? _selectedFolder

            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        var dialog = new OpenFileDialog

        {

            Title = LocalizationManager.Instance.GetString("HizliDosyaAc"),

            InitialDirectory = initialDir,

            Filter = "Tüm Dosyalar|*.*",

            CheckFileExists = true,

            Multiselect = false

        };

        if (dialog.ShowDialog(this) == true)

        {

            OpenFile(dialog.FileName);

        }

    }

    private void MenuItem_ApplyLastAiResponseToFile_Click(object sender, RoutedEventArgs e)

    {

        var activeSession = _chatFlowService.ActiveSession;

        if (activeSession == null)

        {

            MessageBox.Show(LocalizationManager.Instance.GetString("LutfenOnceBirSohbetOturumuBaslatin"), LocalizationManager.Instance.GetString("SonAIYanitiniUygula"), MessageBoxButton.OK, MessageBoxImage.Information);

            return;

        }

        if (!(tcEditor.SelectedItem is TabItem activeTab && activeTab.Tag is TabInfo tabInfo && activeTab.Content is TextEditor editor))

        {

            MessageBox.Show(LocalizationManager.Instance.GetString("LutfenOnceBirDosyaAcin"), LocalizationManager.Instance.GetString("SonAIYanitiniUygula"), MessageBoxButton.OK, MessageBoxImage.Information);

            return;

        }

        var lastAssistant = activeSession.History

            .Where(msg => msg.Role == "assistant" || msg.Role == "tool")

            .Reverse()

            .FirstOrDefault(msg => !string.IsNullOrWhiteSpace(msg.Content));

        if (lastAssistant == null || string.IsNullOrWhiteSpace(lastAssistant.Content))

        {

            MessageBox.Show(LocalizationManager.Instance.GetString("GecerliBirAIYanitiBulunamadi"), LocalizationManager.Instance.GetString("SonAIYanitiniUygula"), MessageBoxButton.OK, MessageBoxImage.Information);

            return;

        }

        var extracted = ExtractCodeFromAiResponse(lastAssistant.Content);

        if (string.IsNullOrWhiteSpace(extracted))

        {

            var useRaw = MessageBox.Show(LocalizationManager.Instance.GetString("AIYanitindanKodCikarilamadiYanitinTumMetniniSeciliDosyayaYazmakIsterMisiniz"), LocalizationManager.Instance.GetString("SonAIYanitiniDosyayaUygula"), MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (useRaw != MessageBoxResult.Yes)

                return;

            extracted = lastAssistant.Content.Trim();

        }

        editor.Text = extracted;

        tabInfo.IsModified = true;

        UpdateTabHeader(activeTab, tabInfo);

        AddTerminalMessage(LocalizationManager.Instance.GetString("SonAIYanitiAktifDosyayaUygulandi"));

    }

    private string? ExtractPathFromText(string text)

    {

        var pattern = @"""(?<path>[A-Za-z]:\\[^""']+)""|(?<path>[A-Za-z]:\\[^\s]+)";

        var match = Regex.Match(text, pattern);

        if (match.Success)

        {

            return match.Groups["path"].Value.TrimEnd('.', ',', ';');

        }

        return null;

    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)

    {

        if (e.Key == Key.Escape && paletteOverlay.Visibility == Visibility.Visible)

        {

            HideCommandPalette();

            e.Handled = true;

            return;

        }

        if (e.Key == Key.Escape && inlineEditOverlay.Visibility == Visibility.Visible)

        {

            HideInlineEdit();

            e.Handled = true;

            return;

        }

        if (e.Key == Key.K && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))

        {

            ShowInlineEdit();

            e.Handled = true;

            return;

        }

        if (e.Key == Key.P && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))

        {

            ShowCommandPalette();

            e.Handled = true;

            return;

        }

        if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))

        {

            SaveCurrentFile();

            e.Handled = true;

            return;

        }

        if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))

        {

            SaveAllTabs();

            e.Handled = true;

            return;

        }

        if (e.Key == Key.L && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))

        {

            BtnClearTerminal_Click(null!, null!);

            e.Handled = true;

            return;

        }

        if (e.Key == Key.T && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))

        {

            txtTerminalInput.Focus();

            txtTerminalInput.SelectAll();

            e.Handled = true;

            return;

        }

    }

    #region Inline Edit (Ctrl+K)

    private TextEditor? _inlineEditActiveEditor;

    private int _inlineEditSelectionStart;

    private int _inlineEditSelectionLength;

    private string _inlineEditSelectedText = "";

    private void ShowInlineEdit()

    {

        if (tcEditor.SelectedItem is not TabItem tabItem) return;

        if (tabItem.Content is not TextEditor editor) return;

        var selectedText = editor.SelectedText;

        if (string.IsNullOrWhiteSpace(selectedText))

        {

            MessageBox.Show(LocalizationManager.Instance.GetString("LutfenDuzenlemekIstediginizKoduSecin"), LocalizationManager.Instance.GetString("IlgiliBilgi"), MessageBoxButton.OK, MessageBoxImage.Information);

            return;

        }

        _inlineEditActiveEditor = editor;

        _inlineEditSelectionStart = editor.SelectionStart;

        _inlineEditSelectionLength = editor.SelectionLength;

        _inlineEditSelectedText = selectedText;

        txtInlineEditPrompt.Text = "";

        inlineEditOverlay.Visibility = Visibility.Visible;

        txtInlineEditPrompt.Focus();

    }

    private void HideInlineEdit()

    {

        inlineEditOverlay.Visibility = Visibility.Collapsed;

        _inlineEditActiveEditor?.Focus();

        _inlineEditActiveEditor = null;

    }

    private void BtnInlineEditCancel_Click(object sender, RoutedEventArgs e)

    {

        HideInlineEdit();

    }

#pragma warning disable VSTHRD100

    private async void BtnInlineEditApply_Click(object sender, RoutedEventArgs e)

    {

        await ExecuteInlineEditAsync();

    }

    private async void TxtInlineEditPrompt_KeyDown(object sender, KeyEventArgs e)

    {

        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))

        {

            e.Handled = true;

            await ExecuteInlineEditAsync();

        }

    }

#pragma warning restore VSTHRD100

    private void InlineEditOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)

    {

        if (e.OriginalSource == inlineEditOverlay)

        {

            HideInlineEdit();

        }

    }

    private async Task ExecuteInlineEditAsync()

    {

        var prompt = txtInlineEditPrompt.Text.Trim();

        if (string.IsNullOrWhiteSpace(prompt)) return;

        if (_apiClient == null)

        {

            MessageBox.Show(LocalizationManager.Instance.GetString("APIVeyaYerelModelBaglantisiBulunamadi"), LocalizationManager.Instance.GetString("Hata"), MessageBoxButton.OK, MessageBoxImage.Error);

            return;

        }

        if (_inlineEditActiveEditor == null) return;

        txtInlineEditPrompt.IsEnabled = false;

        btnInlineEditApply.IsEnabled = false;

        pbInlineEditLoading.Visibility = Visibility.Visible;

        try

        {

            var systemPrompt = "Sen kıdemli bir yazılım uzmanısın. Kullanıcı sana seçtiği bir kod parçasını ve ne yapmak istediğini verecek. Sadece ve sadece istenen değişiklikleri yap. Markdown kullanma, açıklama yapma, sadece doğrudan yer değiştirecek yeni kodu dön. Eğer koda dokunmayacaksan eski kodu aynen döndür.";

            var userPrompt = $"Seçilen kod:\n\n{_inlineEditSelectedText}\n\nKullanıcı İsteği: {prompt}";

            var messages = new List<ExtendedChatMessage>

            {

                new ExtendedChatMessage { Role = "system", Content = systemPrompt },

                new ExtendedChatMessage { Role = "user", Content = userPrompt }

            };

            var response = await _apiClient.SendChatWithToolsAsync(messages, new List<ToolDefinition>());

            if (response?.Usage != null)

            {

                TokenTrackerService.Instance.AddUsage(_apiClient.ModelName, response.Usage);

            }

            var newCode = "";

            if (response?.Choices != null && response.Choices.Count > 0)

            {

                newCode = response.Choices[0]?.Message?.Content ?? "";

            }

            // Temizleme: Eğer AI markdown block formatı ile döndürdüyse temizle.

            if (newCode.StartsWith("```"))

            {

                var lines = newCode.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                if (lines.Length > 1)

                {

                    var cleanLines = new List<string>();

                    for (int i = 1; i < lines.Length; i++)

                    {

                        if (i == lines.Length - 1 && lines[i].StartsWith("```")) continue;

                        cleanLines.Add(lines[i]);

                    }

                    newCode = string.Join(Environment.NewLine, cleanLines);

                }

            }

            if (!string.IsNullOrWhiteSpace(newCode))

            {

                _inlineEditActiveEditor.Document.Replace(_inlineEditSelectionStart, _inlineEditSelectionLength, newCode.TrimEnd());

                AddTerminalMessage($"Satır içi düzenleme uygulandı (Ctrl+K).");

            }

        }

        catch (Exception ex)

        {

            AddTerminalMessage($"Inline Edit Hatası: {ex.Message}");

            MessageBox.Show(LocalizationManager.Instance.GetString("IslemSirasindaHataOlustuExMessage").Replace("{ex.Message}", ex.Message), LocalizationManager.Instance.GetString("Hata"), MessageBoxButton.OK, MessageBoxImage.Error);

        }

        finally

        {

            txtInlineEditPrompt.IsEnabled = true;

            btnInlineEditApply.IsEnabled = true;

            pbInlineEditLoading.Visibility = Visibility.Collapsed;

            HideInlineEdit();

        }

    }

    #endregion

    private async Task<T> RunOnUiThreadAsync<T>(Func<Task<T>> operation)

    {

        if (!Dispatcher.CheckAccess())

        {

            return await Dispatcher.InvokeAsync(() => operation()).Task.Unwrap();

        }

        return await operation();

    }

    private Task<ToolExecutor.ConfirmResult> ShowConfirmCommandWindowAsync(string message)

    {

        try

        {

            var win = new ConfirmCommandWindow(message)

            {

                Owner = this

            };

            win.ShowDialog();

            return Task.FromResult(win.Result);

        }

        catch (Exception ex)

        {

            AddTerminalMessage($"Onay penceresi açılamadı, güvenli varsayılan davranış uygulanıyor: {ex.Message}");

            Notify("Onay penceresi açılamadı; işlem güvenli şekilde iptal edildi.", NotificationSeverity.Warning);

            return Task.FromResult(ToolExecutor.ConfirmResult.Cancel);

        }

    }

    private Task<bool> ShowDiffWindowConfirmAsync(string filePath, string oldContent, string newContent)

    {

        if (!Dispatcher.CheckAccess())

        {

            return Dispatcher.Invoke(() => ShowDiffWindowConfirmAsync(filePath, oldContent, newContent));

        }

        try

        {

            var diffWin = new DiffWindow(filePath, oldContent, newContent)

            {

                Owner = this

            };

            var result = diffWin.ShowDialog() == true;

            return Task.FromResult(result);

        }

        catch (Exception ex)

        {

            AddTerminalMessage($"Diff penceresi açılamadı, güvenli varsayılan davranış uygulanıyor: {ex.Message}");

            Notify("Değişiklik önizleme penceresi açılamadı; değişiklik reddedildi.", NotificationSeverity.Warning);

            return Task.FromResult(false);

        }

    }

    private Task<bool> ShowExternalFolderAccessAsync(string folderPath)
    {
        var result = MessageBox.Show(
            $"AI bu harici klasöre erişmek istiyor:\n\n{folderPath}\n\nBu klasör mevcut oturum boyunca okunabilir ve aranabilir. Dosya değişiklikleri ayrıca onaylanır. İzin verilsin mi?",
            "Harici klasör erişimi",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        return Task.FromResult(result == MessageBoxResult.Yes);
    }

    private Task<string> ShowAskUserDialogAsync(string question, List<string> options, bool allowMultiple = false)

    {

        if (!Dispatcher.CheckAccess())

        {

            return Dispatcher.Invoke(() => ShowAskUserDialogAsync(question, options));

        }

        try

        {

            var dialog = new AskUserDialog(question, options, allowMultiple)

            {

                Owner = this

            };

            if (dialog.ShowDialog() == true)

            {

                var answer = dialog.SelectedAnswer;

                AddTerminalMessage($"Kullanıcı seçimi: {answer}");

                return Task.FromResult(answer);

            }

            return Task.FromResult("Kullanıcı iptal etti.");

        }

        catch (Exception ex)

        {

            AddTerminalMessage($"Soru penceresi açılamadı: {ex.Message}");

            return Task.FromResult("Hata oluştu, devam et.");

        }

    }

    private async Task OpenDocumentWithLspAsync(string filePath, string content, string languageId)

    {

        try

        {

            var lspLanguage = languageId switch
            {
                "python" => "py",
                "typescript" => "ts",
                "javascript" => "js",
                "csharp" => "cs",
                _ => languageId
            };

            var languageServer = GetLanguageServerService(lspLanguage);

            if (!string.IsNullOrEmpty(_selectedFolder) &&
                !languageServer.IsConnected)

            {

                await languageServer.StartServerAsync(lspLanguage, _selectedFolder);

            }

            if (languageServer.IsConnected)

            {

                await languageServer.OpenDocumentAsync(filePath, content, languageId);

            }

            else

            {

                AddTerminalMessage($"[LSP] {Path.GetFileName(filePath)} için LSP sunucusu hazır değil.");

            }

        }

        catch (Exception ex)

        {

            AddTerminalMessage($"[LSP] Dosya açma sırasında hata: {ex.Message}");

        }

    }

    private void OnEditorTextEntered(TextEditor editor, TextCompositionEventArgs e)

    {

        if (string.IsNullOrEmpty(e.Text) || completionPopup.IsOpen)

            return;

        var trigger = e.Text == "." || e.Text == "_" || char.IsLetterOrDigit(e.Text[0]);

        if (!trigger)

            return;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) || Keyboard.Modifiers.HasFlag(ModifierKeys.Windows))

            return;

        RunBackground(ShowCompletionAsync(editor), "ShowCompletion");

    }

    private void OnLspDiagnosticsReceived(object? sender, (string filePath, LanguageDiagnostic[] diagnostics) data)

    {

        var normalizedPath = Path.GetFullPath(data.filePath);

        _lspDiagnostics[normalizedPath] = data.diagnostics;

        // Eğer dosya açıksa editörü güncelle

        foreach (TabItem tabItem in tcEditor.Items)

        {

            if (tabItem.Tag is TabInfo tabInfo && tabItem.Content is TextEditor editor &&

                Path.GetFullPath(tabInfo.FilePath).Equals(normalizedPath, StringComparison.OrdinalIgnoreCase))

            {

                ApplyDiagnosticsToEditor(editor, normalizedPath, editor.Text);

            }

        }

        // Dosya ağacında hata durumunu güncelle

        UpdateTreeViewItemErrorState(normalizedPath, data.diagnostics.Length > 0);

        if (tabDiagnostics?.IsSelected == true)
            RefreshTelemetryPanel();

        // LSP bağlandığında aktif sekme bu dosyaysa badge'i güncelle
        if (tcEditor.SelectedItem is TabItem activeTabItem && activeTabItem.Tag is TabInfo activeTabInfo &&
            Path.GetFullPath(activeTabInfo.FilePath).Equals(normalizedPath, StringComparison.OrdinalIgnoreCase))
        {
            UpdateLspStatusBar(normalizedPath);
        }

    }

    /// <summary>

    /// Akıllı LSP sunucusu başlatma: Proje dosyalarına göre uygun dil sunucusunu bulur

    /// </summary>

    private async System.Threading.Tasks.Task StartSmartLspServerAsync(string projectPath)

    {

        // Proje degisirken eski dil oturumlarini temizle

        DisposeLanguageServerServices();

        // Desteklenen dosya uzantıları ve LSP başlatma önceliği

        var languagePriority = new[] 

        {

            ("dart", new[] { ".dart" }),      // Flutter/Dart: En yüksek öncelik

            ("java", new[] { ".java" }),      // Java

            ("cs", new[] { ".cs" }),          // C#

            ("cpp", new[] { ".cpp", ".cxx", ".cc", ".c", ".hpp", ".h" }), // C++

            ("py", new[] { ".py" }),          // Python

            ("ts", new[] { ".ts", ".tsx" }),  // TypeScript

            ("js", new[] { ".js", ".jsx" }),  // JavaScript

            ("css", new[] { ".css", ".scss", ".sass", ".less" }), // CSS

            ("html", new[] { ".html", ".htm" }), // HTML

            ("go", new[] { ".go" }),              // Go

            ("rs", new[] { ".rs" }),              // Rust

            ("kt", new[] { ".kt", ".kts" }),      // Kotlin

            ("php", new[] { ".php" }),            // PHP

            ("rb", new[] { ".rb" }),              // Ruby

            ("sql", new[] { ".sql" }),            // SQL

            ("swift", new[] { ".swift" })         // Swift

        };

        // Proje dosyalarını tara ve en yaygın dosya türünü bul

        var extensionCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        try

        {

            // Proje klasöründeki tüm dosyaları tara (alt klasörler dahil, .git ve node_modules hariç)

            var allFiles = Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories)

                .Where(f => !f.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar) &&

                           !f.Contains(Path.DirectorySeparatorChar + "node_modules" + Path.DirectorySeparatorChar))

                .Take(100); // İlk 100 dosyaya bak (performans için)

            foreach (var file in allFiles)

            {

                var ext = Path.GetExtension(file).ToLowerInvariant();

                if (!string.IsNullOrEmpty(ext) && ext.Length > 1)

                {

                    var cleanExt = ext.Substring(1); // Baştaki . karakterini kaldır

                    if (extensionCounts.ContainsKey(cleanExt))

                        extensionCounts[cleanExt]++;

                    else

                        extensionCounts[cleanExt] = 1;

                }

            }

        }

        catch

        {

            // Dosya taramasında hata olursa sessizce geç

        }

        // Öncelik sırasına göre LSP sunucusu başlatmayı dene

        bool startedAnyServer = false;

        List<string> missingLanguages = new();

        foreach (var (langExt, extensions) in languagePriority)

        {

            // Bu dil için dosya var mı?

            var hasFiles = extensions.Any(ext => 

                extensionCounts.ContainsKey(ext.TrimStart('.')));

            if (hasFiles)

            {

                AddTerminalMessage(Localization.Format(
                    "[LSP] {0} dosyaları bulundu, LSP başlatılıyor...",
                    "[LSP] {0} files found, starting LSP...",
                    langExt.ToUpperInvariant()));

                var languageServer = GetLanguageServerService(langExt);
                var started = await languageServer.StartServerAsync(langExt, projectPath);

                if (started)

                {

                    startedAnyServer = true;

                    var serverName = langExt switch

                    {

                        "dart" => "Dart Analysis Server",

                        "java" => "Eclipse JDT Language Server",

                        "cs" => "OmniSharp",

                        "cpp" => "clangd",

                        "py" => "Pyright",

                        "ts" => "TypeScript Language Server",

                        "js" => "TypeScript Language Server",

                        "css" => "CSS Language Server",

                        "html" => "HTML Language Server",

                        _ => "Language Server"

                    };

                    AddTerminalMessage(Localization.Get($"[LSP] ✅ {serverName} başarıyla başlatıldı!", $"[LSP] ✅ {serverName} started successfully!"));

                    break;

                }

                else

                {

                    missingLanguages.Add(langExt);

                    AddTerminalMessage(Localization.Get($"[LSP] ❌ {langExt.ToUpperInvariant()} için LSP başlatılamadı. Gerekli program yüklü mü?", $"[LSP] ❌ Could not start LSP for {langExt.ToUpperInvariant()}. Is the required program installed?"));

                }

            }

        }

        if (missingLanguages.Count > 0)

        {

            AddTerminalMessage($"[LSP] Eksik veya hatalı LSP desteği bulunan diller: {string.Join(", ", missingLanguages.Select(x => x.ToUpperInvariant()))}");

            ShowLspMissingToast(missingLanguages.First());

            PromptInstallLspForLanguage(missingLanguages.First(), projectPath);

        }

        else if (!startedAnyServer)

        {

            AddTerminalMessage(Localization.Get("[LSP] Proje için uygun LSP sunucusu bulunamadı veya başlatılamadı.", "[LSP] No suitable LSP server was found or could be started for the project."));

        }

    }

    private void PromptInstallLspForLanguage(string languageExtension, string projectPath)

    {

        var server = PluginManager.LspServerManager.GetAllLspServers()

            .FirstOrDefault(s => s.Id.Equals(languageExtension, StringComparison.OrdinalIgnoreCase)

                || s.FileExtensions.Any(ext => ext.TrimStart('.').Equals(languageExtension, StringComparison.OrdinalIgnoreCase)));

        var friendlyName = languageExtension switch

        {

            "ts" => "TypeScript/JavaScript",

            "js" => "JavaScript",

            "py" => "Python",

            "dart" => "Dart/Flutter",

            "cs" => "C#",

            "cpp" => "C++",

            "css" => "CSS",

            "html" => "HTML",

            _ => languageExtension.ToUpperInvariant()

        };

        var installInfo = GetInstallableLanguageInfo(languageExtension);

        var message = server != null

            ? $"Bu proje için {friendlyName} LSP desteği eksik veya başlatılamadı. {server.Name} kurulumu gerekiyor. Eklenti yöneticisini açmak ister misiniz?"

            : $"Bu proje için {friendlyName} LSP desteği eksik veya başlatılamadı. Eklenti yöneticisini açmak ister misiniz?";

        Dispatcher.Invoke(() =>

        {

            if (installInfo != null)

            {

                var installMessage = $"Bu proje için {friendlyName} LSP desteği eksik veya başlatılamadı. " +

                    $"Otomatik olarak {installInfo.Value.displayName} paketlerini yüklemek ister misiniz?" +

                    "\n\nEvet: Otomatik yükleme\nHayır: Eklenti yöneticisini aç\nİptal: Hiçbir şey yapma";

                var result = MessageBox.Show(this, installMessage, LocalizationManager.Instance.GetString("LSPDestegiEksik"), MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)

                {

                    if (!string.IsNullOrEmpty(projectPath))

                    {

                        RunBackground(InstallLanguageDependenciesAsync(projectPath, installInfo.Value.languageId, installInfo.Value.displayName), "InstallLspDeps");

                    }

                }

                else if (result == MessageBoxResult.No)

                {

                    OpenPluginManagerAndRetry();

                }

                else

                {

                    Notify($"{friendlyName} LSP desteği eksik. Eklentiler sekmesinden manuel olarak ekleyebilirsiniz.", NotificationSeverity.Info);

                }

            }

            else

            {

                var result = MessageBox.Show(this, message, LocalizationManager.Instance.GetString("LSPDestegiEksik"), MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)

                {

                    OpenPluginManagerAndRetry();

                }

                else

                {

                    Notify($"{friendlyName} LSP desteği eksik. Eklentiler sekmesinden manuel olarak ekleyebilirsiniz.", NotificationSeverity.Info);

                }

            }

        });

    }

    private static (string languageId, string displayName)? GetInstallableLanguageInfo(string languageExtension)

    {

        return languageExtension switch

        {

            "ts" => ("typescript", "TypeScript"),

            "js" => ("typescript", "TypeScript"),

            "py" => ("pyright", "Pyright"),

            "html" => ("html-css", "HTML/CSS Language Server"),

            "css" => ("html-css", "HTML/CSS Language Server"),

            _ => null

        };

    }

    private void ShowLspMissingToast(string languageExtension)

    {

        var friendlyName = languageExtension switch

        {

            "ts" => "TypeScript/JavaScript",

            "js" => "JavaScript",

            "py" => "Python",

            "dart" => "Dart/Flutter",

            "cs" => "C#",

            "cpp" => "C++",

            "css" => "CSS",

            "html" => "HTML",

            _ => languageExtension.ToUpperInvariant()

        };

        Notify($"{friendlyName} LSP desteği eksik. Eklentiler sekmesini açmak için buraya tıklayın.", NotificationSeverity.Warning);

    }

    private void OpenPluginManagerAndRetry()

    {

        try

        {
            var pluginWindow = new PluginManagerWindow();
            if (this.IsLoaded && this.IsVisible)
            {
                pluginWindow.Owner = this;
            }
            pluginWindow.ShowDialog();

            if (!string.IsNullOrEmpty(_selectedFolder))

            {

                RunBackground(StartSmartLspServerAsync(_selectedFolder), "StartSmartLspServer");

            }

        }

        catch (Exception ex)

        {

            MessageBox.Show(this, LocalizationManager.Instance.GetString("EklentiYoneticisiAcilamadiExMessage").Replace("{ex.Message}", ex.Message), LocalizationManager.Instance.GetString("Hata"), MessageBoxButton.OK, MessageBoxImage.Error);

        }

    }

    private async Task InstallLanguageDependenciesAsync(string projectPath, string languageId, string displayName)

    {

        if (_terminalService == null)

        {

            Notify("Terminal servisi devre dışı, otomatik yükleme yapılamıyor.", NotificationSeverity.Error);

            return;

        }

        if (_terminalService.IsBusy)

        {

            Notify("Terminal şu anda meşgul, otomatik yükleme yapılamıyor.", NotificationSeverity.Warning);

            return;

        }

        Notify($"{displayName} paketleri yükleniyor... Lütfen bekleyin.", NotificationSeverity.Info);

        try

        {

            var progress = new Progress<(double, string)>(update =>

            {

                AddTerminalMessage($"[Yükleme] {update.Item2}");

            });

            string? error = await DependencyInstaller.InstallForLanguageAsync(projectPath, languageId, progress);

            if (string.IsNullOrEmpty(error))

            {

                Notify($"{displayName} paketleri yüklendi. LSP yeniden deneniyor.", NotificationSeverity.Success);

                RunBackground(StartSmartLspServerAsync(projectPath), "StartSmartLspServer");

            }

            else

            {

                Notify($"Otomatik yükleme başarısız oldu: {error}", NotificationSeverity.Error);

            }

        }

        catch (Exception ex)

        {

            Notify($"Otomatik yükleme sırasında hata oluştu: {ex.Message}", NotificationSeverity.Error);

        }

    }

    private async Task InstallTypeScriptDependenciesAsync(string projectPath)

    {

        await InstallLanguageDependenciesAsync(projectPath, "typescript", "TypeScript");

    }

    public void OpenProjectFolder(string folderPath)

    {

        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath)) return;

        _selectedFolder = folderPath;

        UpdateTerminalPrompt();

        _cachedPlanResult = null;   // yeni proje açılınca eski plan temizlenir

        CloseAllTabs();             // yeni proje açılınca eski projenin sekmelerini kapat

        TokenTrackerService.Instance.SetProject(folderPath);   // token istatistiklerini bu proje için yükle
        RefreshTelemetryPanel();

        noProjectWarningBanner.Visibility = Visibility.Collapsed;

        LoadFileTree(_selectedFolder);

        InitializeToolExecutor(); // ToolExecutor'u yeni klasörle güncelle

        _chatFlowService.UpdateClients(_apiClient, _toolExecutor);

        AddTerminalMessage(Localization.Get($"Klasör seçildi: {_selectedFolder}", $"Folder selected: {_selectedFolder}"));

        var detectedProjectType = ProjectTypeDetector.DetectProjectType(_selectedFolder);

        var projectTypeLabel = detectedProjectType switch

        {

            "Flutter" => "Flutter",

            "Node" => "Node.js",

            "DotNet" => ".NET",

            "Python" => "Python",

            "Web" => "Web",

            "C/C++" => "C/C++",

            "Rust" => "Rust",

            "Go" => "Go",

            _ => "Genel Proje"

        };

        tbProjectType.Text = $"📁 {projectTypeLabel}";

        // Show toast notification

        Notify($"Proje klasörü seçildi: {Path.GetFileName(_selectedFolder)}", NotificationSeverity.Success);

        _chatFlowService.ReloadSessions(); // Proje oturumlarını yükle

        RefreshChatSessionsList();

        // Arka planda diagnostics (hata/uyarı) taramasını başlat

        RunBackground(ScanProjectDiagnosticsAsync(_selectedFolder), "ScanProjectDiagnostics");

        // Initialize Git service

        _gitService.Initialize(_selectedFolder);

        // Akıllı LSP başlatma: Proje dosyalarına göre uygun LSP sunucusunu bul ve başlat

        RunBackground(StartSmartLspServerAsync(_selectedFolder), "StartSmartLspServer");

    }

    private void BtnSelectFolder_Click(object sender, RoutedEventArgs e)

    {

        var dialog = new OpenFolderDialog

        {

            Title = LocalizationManager.Instance.GetString("ProjeKlasorunuSec"),

            Multiselect = false

        };

        if (dialog.ShowDialog() == true)

        {

            OpenProjectFolder(dialog.FolderName);

        }

    }

    public class GitFileChange

    {

        public string FilePath { get; set; } = "";

        public string Status { get; set; } = "";

    }

    private void TvFileTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)

    {

        // Sağ tıklanan elemanı otomatik olarak seç

        if (e.OriginalSource is DependencyObject dep)

        {

            while (dep != null && !(dep is TreeViewItem))

            {

                dep = VisualTreeHelper.GetParent(dep);

            }

            if (dep is TreeViewItem item)

            {

                item.IsSelected = true;

                item.Focus();

                e.Handled = true;

            }

        }

    }

    private void MenuItem_NewFile_Click(object sender, RoutedEventArgs e)

    {

        if (tvFileTree.SelectedItem is TreeViewItem selectedItem && selectedItem.Tag is string targetPath)

        {

            var directory = Directory.Exists(targetPath) ? targetPath : Path.GetDirectoryName(targetPath);

            if (string.IsNullOrEmpty(directory)) return;

            var inputDlg = new InputDialog("Yeni dosya adını uzantısıyla yazın (örn: deneme.py):", "Yeni Dosya", "")

            {

                Owner = this

            };

            if (inputDlg.ShowDialog() == true)

            {

                var newFileName = inputDlg.InputText.Trim();

                if (string.IsNullOrEmpty(newFileName)) return;

                var newFilePath = Path.Combine(directory, newFileName);

                try

                {

                    if (File.Exists(newFilePath))

                    {

                        MessageBox.Show("Bu dosya zaten mevcut!", LocalizationManager.Instance.GetString("Hata"), MessageBoxButton.OK, MessageBoxImage.Error);

                        return;

                    }

                    File.WriteAllText(newFilePath, "");

                    AddTerminalMessage($"Yeni dosya oluşturuldu: {newFilePath}");

                    // Dosya ağacını yenile

                    if (!string.IsNullOrEmpty(_selectedFolder)) LoadFileTree(_selectedFolder);

                }

                catch (Exception ex)

                {

                    MessageBox.Show(LocalizationManager.Instance.GetString("DosyaOlusturulamadiExMessage").Replace("{ex.Message}", ex.Message), LocalizationManager.Instance.GetString("Hata"), MessageBoxButton.OK, MessageBoxImage.Error);

                }

            }

        }

        else

        {

            MessageBox.Show(LocalizationManager.Instance.GetString("LutfenOnceListedenBirDosyaVeyaKlasorSecin"), LocalizationManager.Instance.GetString("IlgiliBilgi"), MessageBoxButton.OK, MessageBoxImage.Information);

        }

    }

    private void MenuItem_NewFolder_Click(object sender, RoutedEventArgs e)

    {

        if (tvFileTree.SelectedItem is TreeViewItem selectedItem && selectedItem.Tag is string targetPath)

        {

            var directory = Directory.Exists(targetPath) ? targetPath : Path.GetDirectoryName(targetPath);

            if (string.IsNullOrEmpty(directory)) return;

            var inputDlg = new InputDialog("Yeni klasör adını yazın:", "Yeni Klasör", "")

            {

                Owner = this

            };

            if (inputDlg.ShowDialog() == true)

            {

                var newFolderName = inputDlg.InputText.Trim();

                if (string.IsNullOrEmpty(newFolderName)) return;

                var newFolderPath = Path.Combine(directory, newFolderName);

                try

                {

                    if (Directory.Exists(newFolderPath))

                    {

                        MessageBox.Show(LocalizationManager.Instance.GetString("BuKlasorZatenMevcut"), "Hata", MessageBoxButton.OK, MessageBoxImage.Error);

                        return;

                    }

                    Directory.CreateDirectory(newFolderPath);

                    AddTerminalMessage($"Yeni klasör oluşturuldu: {newFolderPath}");

                    // Dosya ağacını yenile

                    if (!string.IsNullOrEmpty(_selectedFolder)) LoadFileTree(_selectedFolder);

                }

                catch (Exception ex)

                {

                    MessageBox.Show(LocalizationManager.Instance.GetString("KlasorOlusturulamadiExMessage").Replace("{ex.Message}", ex.Message), LocalizationManager.Instance.GetString("Hata"), MessageBoxButton.OK, MessageBoxImage.Error);

                }

            }

        }

        else

        {

            MessageBox.Show(LocalizationManager.Instance.GetString("LutfenOnceListedenBirDosyaVeyaKlasorSecin"), LocalizationManager.Instance.GetString("IlgiliBilgi"), MessageBoxButton.OK, MessageBoxImage.Information);

        }

    }

    private void MenuItem_Delete_Click(object sender, RoutedEventArgs e)

    {

        if (tvFileTree.SelectedItem is TreeViewItem selectedItem && selectedItem.Tag is string targetPath)

        {

            var isDir = Directory.Exists(targetPath);

            var isFile = File.Exists(targetPath);

            if (!isDir && !isFile) return;

            var typeName = isDir ? "klasörü" : "dosyayı";

            var result = MessageBox.Show(

                $"Bu {typeName} kalıcı olarak silmek istediğinize emin misiniz?\n\nYol: {targetPath}",

                "Silme Onayı",

                MessageBoxButton.YesNo,

                MessageBoxImage.Warning

            );

            if (result == MessageBoxResult.Yes)

            {

                try

                {

                    if (isDir)

                    {

                        Directory.Delete(targetPath, true);

                        AddTerminalMessage($"Klasör silindi: {targetPath}");

                    }

                    else

                    {

                        File.Delete(targetPath);

                        AddTerminalMessage($"Dosya silindi: {targetPath}");

                        // Eğer silinen dosya editörde açıksa sekmesini kapat

                        CloseTabIfOpen(targetPath);

                    }

                    // Dosya ağacını yenile

                    if (!string.IsNullOrEmpty(_selectedFolder)) LoadFileTree(_selectedFolder);

                }

                catch (Exception ex)

                {

                    MessageBox.Show(LocalizationManager.Instance.GetString("SilmeIslemiBasarisizOlduExMessage").Replace("{ex.Message}", ex.Message), LocalizationManager.Instance.GetString("Hata"), MessageBoxButton.OK, MessageBoxImage.Error);

                }

            }

        }

    }

    private void CloseTabIfOpen(string filePath)

    {

        var fullPath = Path.GetFullPath(filePath);

        TabItem? tabToClose = null;

        foreach (TabItem tabItem in tcEditor.Items)

        {

            if (tabItem.Tag is TabInfo tabInfo && Path.GetFullPath(tabInfo.FilePath).Equals(fullPath, StringComparison.OrdinalIgnoreCase))

            {

                tabToClose = tabItem;

                break;

            }

        }

        if (tabToClose != null)

        {

            tcEditor.Items.Remove(tabToClose);

            if (tcEditor.Items.Count == 0)

            {

                tbCurrentFile.Text = "📄 Dosya Açık Değil";

            }

        }

    }

    private void CloseAllTabs()

    {

        tcEditor.Items.Clear();

        if (tcEditorRight != null)

        {

            tcEditorRight.Items.Clear();

            tcEditorRight.Visibility = Visibility.Collapsed;

            _isSplitEditorActive = false;

            if (btnSplitEditor != null)

            {

                btnSplitEditor.ClearValue(Button.BackgroundProperty);

                btnSplitEditor.ClearValue(Button.BorderBrushProperty);

            }

        }

        if (tbCurrentFile != null)

        {

            tbCurrentFile.Text = LocalizationManager.Instance.GetString("DosyaAcikDegil");

        }

    }

    private void MenuItem_ShowInExplorer_Click(object sender, RoutedEventArgs e)

    {

        if (tvFileTree.SelectedItem is TreeViewItem selectedItem && selectedItem.Tag is string targetPath)

        {

            var directory = Directory.Exists(targetPath) ? targetPath : Path.GetDirectoryName(targetPath);

            if (string.IsNullOrEmpty(directory)) return;

            try

            {

                Process.Start(new ProcessStartInfo

                {

                    FileName = "explorer.exe",

                    Arguments = $"\"{directory}\"",

                    UseShellExecute = true

                });

            }

            catch (Exception ex)

            {

                MessageBox.Show(LocalizationManager.Instance.GetString("KlasorAcilamadiExMessage").Replace("{ex.Message}", ex.Message), LocalizationManager.Instance.GetString("Hata"), MessageBoxButton.OK, MessageBoxImage.Error);

            }

        }

    }

    private void MenuItem_RefreshTree_Click(object sender, RoutedEventArgs e)

    {

        if (string.IsNullOrEmpty(_selectedFolder) || !Directory.Exists(_selectedFolder))

        {

            MessageBox.Show(LocalizationManager.Instance.GetString("LutfenOnceBirProjeKlasoruSecin"), LocalizationManager.Instance.GetString("YenilemeYapilamiyor"), MessageBoxButton.OK, MessageBoxImage.Warning);

            return;

        }

        AddTerminalMessage(LocalizationManager.Instance.GetString("DosyaAgaciVeProjeTanilariYenileniyor"));

        LoadFileTree(_selectedFolder);

        RunBackground(ScanProjectDiagnosticsAsync(_selectedFolder), "ScanProjectDiagnostics");

        RefreshOpenEditorDiagnostics();

        AddTerminalMessage(Localization.Get("Yenileme tamamlandı.", "Refresh completed."));

    }

    private void RefreshOpenEditorDiagnostics()

    {

        if (!Dispatcher.CheckAccess())

        {

            Dispatcher.Invoke(RefreshOpenEditorDiagnostics);

            return;

        }

        foreach (TabItem tabItem in tcEditor.Items)

        {

            if (tabItem.Tag is TabInfo tabInfo && tabItem.Content is TextEditor editor)

            {

                var normalizedFilePath = Path.GetFullPath(tabInfo.FilePath);

                ApplyDiagnosticsToEditor(editor, normalizedFilePath, editor.Text);

                var hasError = _fileDiagnostics.TryGetValue(normalizedFilePath, out var diagnostics) && diagnostics.Count > 0;

                UpdateTreeViewItemErrorState(normalizedFilePath, hasError);

            }

        }

    }

    private void LoadFileTree(string folderPath)

    {

        var displayName = Path.GetFileName(folderPath);

        if (string.IsNullOrEmpty(displayName))

        {

            displayName = folderPath;

        }

        // Eğer kök dizin zaten TreeView'da varsa, zekice yenile (UI kapanmaz, state bozulmaz)

        if (tvFileTree.Items.Count > 0 && tvFileTree.Items[0] is TreeViewItem existingRoot && string.Equals(existingRoot.Tag as string, folderPath, StringComparison.OrdinalIgnoreCase))

        {

            RefreshNode(existingRoot, folderPath);

            return;

        }

        // İlk kez yükleniyorsa

        tvFileTree.Items.Clear();

        var rootItem = CreateDirectoryItem(folderPath, displayName);

        tvFileTree.Items.Add(rootItem);

        // Root dizinin alt öğelerini doğrudan yükle ki yenilendiğinde dosyalar hemen görünsün

        rootItem.Items.Clear();

        LoadSubItems(rootItem, folderPath);

        // IsExpanded = true'nun Layout sonrası düzgün çalışması için gecikmeli çalıştır

        _ = Dispatcher.InvokeAsync(() => 

        {

            rootItem.IsExpanded = true;

        }, System.Windows.Threading.DispatcherPriority.Loaded);

    }

    private void RefreshNode(TreeViewItem node, string fullPath)

    {

        if (!Directory.Exists(fullPath)) return;

        // Henüz hiç genişletilmemişse ve sadece "..." içeriyorsa atla

        if (node.Items.Count == 1 && node.Items[0] is string) return;

        try

        {

            var allExistingItems = node.Items.OfType<TreeViewItem>().Where(i => i.Tag is string).ToList();

            var actualDirs = Directory.GetDirectories(fullPath)

                .Where(d => {

                    var dirName = Path.GetFileName(d);

                    return !(dirName.Equals(".git", StringComparison.OrdinalIgnoreCase) ||

                             dirName.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||

                             dirName.Equals("bin", StringComparison.OrdinalIgnoreCase) ||

                             dirName.Equals("obj", StringComparison.OrdinalIgnoreCase) ||

                             dirName.Equals(".vs", StringComparison.OrdinalIgnoreCase));

                }).ToList();

            var actualFiles = Directory.GetFiles(fullPath);

            // Silinmiş öğeleri kaldır (Artık diskte olmayanlar)

            foreach (var item in allExistingItems)

            {

                var itemPath = item.Tag as string;

                if (!actualDirs.Contains(itemPath, StringComparer.OrdinalIgnoreCase) && 

                    !actualFiles.Contains(itemPath, StringComparer.OrdinalIgnoreCase))

                {

                    node.Items.Remove(item);

                }

            }

            // Yeni dizinleri ekle

            foreach (var ad in actualDirs)

            {

                if (!allExistingItems.Any(ed => string.Equals(ed.Tag as string, ad, StringComparison.OrdinalIgnoreCase)))

                {

                    node.Items.Add(CreateDirectoryItem(ad, Path.GetFileName(ad)));

                }

            }

            // Yeni dosyaları ekle

            foreach (var af in actualFiles)

            {

                if (!allExistingItems.Any(ef => string.Equals(ef.Tag as string, af, StringComparison.OrdinalIgnoreCase)))

                {

                    node.Items.Add(CreateFileItem(af, Path.GetFileName(af)));

                }

            }

            // Açık olan alt dizinleri recursive olarak yenile

            foreach (var ed in node.Items.OfType<TreeViewItem>().Where(i => i.IsExpanded && i.Tag is string t && Directory.Exists(t)))

            {

                RefreshNode(ed, ed.Tag as string);

            }

        }

        catch { }

    }

    private TreeViewItem CreateDirectoryItem(string fullPath, string displayName)

    {

        var item = new TreeViewItem

        {

            Header = displayName,

            Tag = fullPath

        };

        item.Items.Add("...");

        return item;

    }

    private TreeViewItem CreateFileItem(string fullPath, string displayName)

    {

        var normalizedPath = Path.GetFullPath(fullPath);

        var item = new TreeViewItem

        {

            Header = displayName,

            Tag = normalizedPath

        };

        // Kaydedilmiş hata durumuna göre rengi ayarla

        if (_fileErrorStates.TryGetValue(normalizedPath, out bool hasError) && hasError)

        {

            item.Foreground = Brushes.Red;

        }

        return item;

    }

    private void LoadSubItems(TreeViewItem parentItem, string fullPath)

    {

        try

        {

            var directories = Directory.GetDirectories(fullPath);

            foreach (var dir in directories)

            {

                var dirName = Path.GetFileName(dir);

                if (dirName.Equals(".git", StringComparison.OrdinalIgnoreCase) ||

                    dirName.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||

                    dirName.Equals("bin", StringComparison.OrdinalIgnoreCase) ||

                    dirName.Equals("obj", StringComparison.OrdinalIgnoreCase) ||

                    dirName.Equals(".vs", StringComparison.OrdinalIgnoreCase))

                {

                    continue;

                }

                var dirItem = CreateDirectoryItem(dir, dirName);

                parentItem.Items.Add(dirItem);

            }

            var files = Directory.GetFiles(fullPath);

            foreach (var file in files)

            {

                var fileItem = CreateFileItem(file, Path.GetFileName(file));

                parentItem.Items.Add(fileItem);

            }

        }

        catch (Exception ex)

        {

            AddTerminalMessage($"Klasör okunurken hata oluştu: {ex.Message}");

        }

    }

    private void TvFileTree_Expanded(object sender, RoutedEventArgs e)

    {

        var item = e.OriginalSource as TreeViewItem;

        if (item != null && item.Items.Count == 1 && item.Items[0] is string placeholder && placeholder == "...")

        {

            item.Items.Clear();

            var fullPath = item.Tag as string;

            if (!string.IsNullOrEmpty(fullPath) && Directory.Exists(fullPath))

            {

                LoadSubItems(item, fullPath);

            }

        }

    }

    private void TvFileTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)

    {

        if (tvFileTree.SelectedItem is TreeViewItem selectedItem)

        {

            var path = selectedItem.Tag as string;

            if (path != null && File.Exists(path))

            {

                OpenFile(path);

            }

        }

    }

    public class TabInfo

    {

        public string FilePath { get; set; } = "";

        public string DisplayName { get; set; } = "";

        public bool IsUntitled { get; set; }

        public bool IsModified { get; set; }

        public FoldingManager? FoldingManager { get; set; }

        public BraceFoldingStrategy? FoldingStrategy { get; set; }

        public BasucuIDE.Controls.ScrollbarOverviewMargin? OverviewMargin { get; set; }

    }

    private void OpenFile(string filePath)

    {

        try
        {
            var fullPath = Path.GetFullPath(filePath);

            // Görsel dosyası mı kontrol et
            var ext = Path.GetExtension(fullPath).ToLowerInvariant();
            if (ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".ico")
            {
                var viewer = new ImageViewerWindow(fullPath);
                viewer.Owner = this;
                viewer.Show();
                return;
            }

            // Sekmenin zaten açık olup olmadığını kontrol et

            TabItem? existingTab = null;

            foreach (TabItem item in tcEditor.Items)

            {

                if (item.Tag is TabInfo tabInfo && Path.GetFullPath(tabInfo.FilePath).Equals(fullPath, StringComparison.OrdinalIgnoreCase))

                {

                    existingTab = item;

                    break;

                }

            }

            if (existingTab != null)

            {

                tcEditor.SelectedItem = existingTab;

            }

            else

            {

                var newTab = CreateTabItem(fullPath);

                tcEditor.Items.Add(newTab);

                tcEditor.SelectedItem = newTab;

            }

            _currentOpenFile = fullPath;

            UpdateBreadcrumb(fullPath);

        }

        catch (Exception ex)

        {

            AddTerminalMessage($"Dosya okunamadı: {ex.Message}");

            MessageBox.Show(LocalizationManager.Instance.GetString("DosyaOkunamadiExMessage").Replace("{ex.Message}", ex.Message), LocalizationManager.Instance.GetString("Hata"), MessageBoxButton.OK, MessageBoxImage.Error);

        }

    }

    private TabItem CreateTabItem(string filePath)

    {

        var tabInfo = new TabInfo { FilePath = filePath, IsModified = false };

        var tabItem = new TabItem

        {

            Tag = tabInfo

        };

        // Sekme Başlığı (Tab Header Layout)

        var headerStack = new StackPanel { Orientation = Orientation.Horizontal };

        var iconText = new TextBlock

        {

            Text = "📄",

            VerticalAlignment = VerticalAlignment.Center,

            Margin = new Thickness(0,0,6,0)

        };

        var headerText = new TextBlock
        {
            Text = Path.GetFileName(filePath),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
            // Removed forced Foreground, relies on TabItem styling
        };

        var closeButton = new TextBlock
        {
            Text = "✕",
            Margin = new Thickness(0, 0, 0, 0),
            Padding = new Thickness(2),
            Width = 16,
            Height = 16,
            Background = Brushes.Transparent,
            Foreground = Brushes.Gray,
            Cursor = Cursors.Hand,
            Tag = tabItem,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Sekmeyi Kapat"
        };

        closeButton.MouseLeftButtonDown += BtnCloseTab_Click;
        closeButton.MouseEnter += (s, e) => closeButton.Foreground = Brushes.White;
        closeButton.MouseLeave += (s, e) => closeButton.Foreground = Brushes.Gray;

        headerStack.Children.Add(iconText);
        headerStack.Children.Add(headerText);
        headerStack.Children.Add(closeButton);

        tabItem.Header = headerStack;

        // Sekme İçeriği (AvalonEdit TextEditor)

        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        // AddTerminalMessage($"📄 Dosya uzantısı: {ext}");

        IHighlightingDefinition? highlighting = null;

        // Hangi dosya türü için hangi XSHD dosyasını yükleyeceğimizi belirt

        var xshdFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)

        {

            { ".html", "HTML.xshd" },

            { ".htm", "HTML.xshd" },

            { ".css", "CSS.xshd" },

            { ".scss", "CSS.xshd" },

            { ".sass", "CSS.xshd" },

            { ".js", "JavaScript.xshd" },

            { ".jsx", "JavaScript.xshd" },

            { ".ts", "JavaScript.xshd" },

            { ".tsx", "JavaScript.xshd" },

            { ".mjs", "JavaScript.xshd" },

            { ".cjs", "JavaScript.xshd" },

            { ".py", "Python.xshd" },

            { ".pyw", "Python.xshd" },

            { ".cs", "CSharp.xshd" },

            { ".csx", "CSharp.xshd" },

            { ".dart", "Dart.xshd" },

            { ".md", "Generic.xshd" },

            { ".yaml", "Generic.xshd" },

            { ".yml", "Generic.xshd" },

            { ".json", "Generic.xshd" },

            { ".xml", "Generic.xshd" },

            { ".kts", "Generic.xshd" },

            { ".sh", "Generic.xshd" },

            { ".bat", "Generic.xshd" },

            { ".txt", "Generic.xshd" }

        };

        if (xshdFiles.TryGetValue(ext, out var xshdFileName))

        {

            try

            {

                // AddTerminalMessage($"🔍 {xshdFileName} için arama başlıyor...");

                // Proje dizinindeki Themes/Highlighting/<dosya>.xshd dosyasını bul

                var projectDir = AppContext.BaseDirectory;

                // AddTerminalMessage($"📂 Assembly konumu: {projectDir}");

                var xshdPath = Path.Combine(projectDir, "Themes", "Highlighting", xshdFileName);

                // AddTerminalMessage($"📂 Denenen yol 1: {xshdPath} (Mevcut: {File.Exists(xshdPath)})");

                // Eğer çıkış dizininde yoksa kaynak dosyasını dene

                if (!File.Exists(xshdPath))

                {

                    var sourceXshdPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Themes", "Highlighting", xshdFileName);

                    // AddTerminalMessage($"📂 Denenen yol 2: {sourceXshdPath} (Mevcut: {File.Exists(sourceXshdPath)})");

                    if (File.Exists(sourceXshdPath))

                    {

                        xshdPath = sourceXshdPath;

                    }

                }

                if (File.Exists(xshdPath))

                {

                    // AddTerminalMessage($"🎨 Özel {Path.GetFileNameWithoutExtension(xshdFileName)} şablonu yüklendi: {xshdPath}");

                    using var reader = new System.Xml.XmlTextReader(xshdPath);

                    var xshd = HighlightingLoader.LoadXshd(reader);

                    // null resolver: built-in tanımlar XSHD renklerini ezmez

                    highlighting = HighlightingLoader.Load(xshd, null);

                    // AddTerminalMessage($"✅ Şablon yüklendi! [{highlighting.Name}]");

                }

                else

                {

                    // AddTerminalMessage($"❌ {xshdFileName} bulunamadı! Varsayılan kullanılacak.");

                }

            }

            catch (Exception ex)

            {

                AddTerminalMessage($"❌ {xshdFileName} yüklenirken hata: {ex.Message}");

                // AddTerminalMessage($"📋 Hata detayı: {ex.StackTrace}");

            }

        }

        // Eğer özel şablon yoksa Generic.xshd'yi denemeye zorla

        if (highlighting == null)

        {

            try

            {

                var projectDir = AppContext.BaseDirectory;

                var genericPath = Path.Combine(projectDir, "Themes", "Highlighting", "Generic.xshd");

                if (!File.Exists(genericPath))

                    genericPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Themes", "Highlighting", "Generic.xshd");

                if (File.Exists(genericPath))

                {

                    using var reader = new System.Xml.XmlTextReader(genericPath);

                    var xshd = HighlightingLoader.LoadXshd(reader);

                    highlighting = HighlightingLoader.Load(xshd, null);

                    // AddTerminalMessage("🎨 Evrensel (Generic) vurgulama kullanıldı!");

                }

            }

            catch { /* Hata olursa fallback yap */ }

        }

        // Generic de bulunamazsa AvalonEdit varsayılanına dön

        if (highlighting == null)

        {

            highlighting = HighlightingManager.Instance.GetDefinitionByExtension(ext);

            // AddTerminalMessage($"🎨 Varsayılan vurgulama kullanıldı: {highlighting?.Name ?? "bilinmiyor"}");

        }

        var editor = new TextEditor

        {

            FontFamily = new FontFamily("Consolas, Courier New, monospace"),

            FontSize = 14,

            ShowLineNumbers = true,

            SyntaxHighlighting = highlighting,

            Background = (SolidColorBrush)FindResource("SurfaceBrush"),

            Foreground = (SolidColorBrush)FindResource("TextPrimaryBrush"),

            LineNumbersForeground = (SolidColorBrush)FindResource("TextSecondaryBrush"),

            WordWrap = false,

            Tag = tabItem

        };

        editor.Options.ConvertTabsToSpaces = false;

        editor.Options.EnableHyperlinks = false;

        editor.Options.HighlightCurrentLine = true;

        // Sağ tık menüsü (ContextMenu)

        var contextMenu = new ContextMenu();

        var menuInlineEdit = new MenuItem { Header = "✨ AI ile Düzenle (Ctrl+K)" };

        menuInlineEdit.Click += (s, e) => {

            if (tcEditor.SelectedItem is TabItem t && t.Content == editor)

                ShowInlineEdit();

        };

        contextMenu.Items.Add(menuInlineEdit);

        contextMenu.Items.Add(new Separator());

        var menuCopy = new MenuItem { Header = "Kopyala", Command = ApplicationCommands.Copy };

        var menuPaste = new MenuItem { Header = "Yapıştır", Command = ApplicationCommands.Paste };

        contextMenu.Items.Add(menuCopy);

        contextMenu.Items.Add(menuPaste);

        editor.ContextMenu = contextMenu;

        editor.Load(filePath);

        // Code Folding (Kod Katlama)

        var foldingManager = FoldingManager.Install(editor.TextArea);

        var foldingStrategy = new BraceFoldingStrategy();

        foldingStrategy.UpdateFoldings(foldingManager, editor.Document);

        tabInfo.FoldingManager = foldingManager;

        tabInfo.FoldingStrategy = foldingStrategy;

        // Dosya ilk yüklendiğinde hata kontrolü yap

        var initialContent = editor.Text;

        var normalizedFilePath = Path.GetFullPath(filePath);

        ApplyDiagnosticsToEditor(editor, normalizedFilePath, initialContent);

        var initialHasError = _fileDiagnostics.TryGetValue(normalizedFilePath, out var initialDiags) && initialDiags.Count > 0;

        UpdateTreeViewItemErrorState(normalizedFilePath, initialHasError);

        editor.PreviewKeyDown += TxtCodeEditor_PreviewKeyDown;

        editor.TextArea.TextEntered += (s, e) => OnEditorTextEntered(editor, e);

        editor.TextArea.TextEntering += (s, e) =>

        {

            if (completionPopup.IsOpen && !string.IsNullOrEmpty(e.Text) && !char.IsLetterOrDigit(e.Text[0]) && e.Text != "_")

            {

                completionPopup.IsOpen = false;

            }

        };

        editor.Document.Changed += (s, e) => TxtCodeEditor_DocumentChanged(editor, tabItem);

        // LSP için dosyayı aç

        var langExt = Path.GetExtension(filePath).ToLowerInvariant().TrimStart('.');

        var languageId = LspLanguageRegistry.TryGet(langExt, out var languageDefinition)
            ? languageDefinition.LanguageId
            : LspLanguageRegistry.NormalizeExtension(langExt);

        _documentVersions[normalizedFilePath] = 1;

        RunBackground(OpenDocumentWithLspAsync(filePath, initialContent, languageId), "OpenDocumentWithLsp");

        // Ctrl+F ile açılan AvalonEdit yerleşik arama paneli

        var panel = SearchPanel.Install(editor);

        ApplySearchPanelStyle(panel);

        // Scrollbar Overview Margin (Canlı LSP Hata & Ctrl+F Arama Çizgileri)
        editor.Loaded += (s, e) =>
        {
            try
            {
                var layer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(editor);
                if (layer != null)
                {
                    var overviewMargin = new BasucuIDE.Controls.ScrollbarOverviewMargin(editor);
                    tabInfo.OverviewMargin = overviewMargin;
                    layer.Add(new BasucuIDE.Controls.OverviewMarginAdorner(editor, overviewMargin));

                    if (_fileDiagnostics.TryGetValue(normalizedFilePath, out var initialDiags))
                    {
                        overviewMargin.SetDiagnostics(initialDiags);
                    }
                }
            }
            catch { }
        };

        tabItem.Content = editor;

        return tabItem;

    }

    private void TxtCodeEditor_DocumentChanged(TextEditor editor, TabItem tabItem)

    {

        if (tabItem.Tag is TabInfo tabInfo)

        {

            if (!tabInfo.IsModified)

            {

                tabInfo.IsModified = true;

                Dispatcher.Invoke(() => UpdateTabHeader(tabItem, tabInfo));

            }

            // Her değişiklikte hata denetimi yap

            var currentContent = editor.Text;

            var normalizedFilePath = Path.GetFullPath(tabInfo.FilePath);

            ApplyDiagnosticsToEditor(editor, normalizedFilePath, currentContent);

            var hasError = _fileDiagnostics.TryGetValue(normalizedFilePath, out var diagnostics) && diagnostics.Count > 0;

            UpdateTreeViewItemErrorState(normalizedFilePath, hasError);

            // Her değişiklikte folding'leri güncelle

            if (tabInfo.FoldingManager != null && tabInfo.FoldingStrategy != null)

            {

                tabInfo.FoldingStrategy.UpdateFoldings(tabInfo.FoldingManager, editor.Document);

            }

            // LSP için değişikliği gönder

            if (_documentVersions.TryGetValue(normalizedFilePath, out var currentVersion))

            {

                var newVersion = currentVersion + 1;

                _documentVersions[normalizedFilePath] = newVersion;

                var languageExtension = GetLspLanguageExtension(normalizedFilePath);
                if (!string.IsNullOrEmpty(languageExtension) && _languageServerServices.TryGetValue(languageExtension, out var languageServer))
                {
                    RunBackground(languageServer.ChangeDocumentAsync(normalizedFilePath, currentContent, newVersion), "LspChangeDoc");
                }

            }

        }

    }

    private void UpdateTabHeader(TabItem tabItem, TabInfo tabInfo)

    {

        if (tabItem.Header is StackPanel headerStack && headerStack.Children.Count > 1 && headerStack.Children[1] is TextBlock headerText)

        {

            var fileName = string.IsNullOrWhiteSpace(tabInfo.DisplayName) ? Path.GetFileName(tabInfo.FilePath) : tabInfo.DisplayName;

            headerText.Text = tabInfo.IsModified ? $"{fileName} *" : fileName;

            headerText.Foreground = tabInfo.IsModified ? Brushes.Orange : (SolidColorBrush)FindResource("DarkText");

        }

    }

    private void ResetTabHeaderModifiedState(TabItem tabItem, TabInfo tabInfo)

    {

        tabInfo.IsModified = false;

        UpdateTabHeader(tabItem, tabInfo);

    }

    // Eklenti sistemini kullanarak dosya içeriğini kontrol eder

    private bool CheckFileForErrors(string filePath, string content)

    {

        try

        {

            // Uygun eklentiyi bul

            var plugin = PluginManager.GetPluginForFile(filePath);

            if (plugin != null)

            {

                // Eklentiyle hata denetimi yap

                var diagnostics = plugin.GetDiagnostics(filePath, content);

                return diagnostics.Count > 0;

            }

            // Eğer uygun eklenti yoksa, varsayılan basit kontrolleri yap

            var ext = Path.GetExtension(filePath).ToLowerInvariant();

            // Yaygın dosya türleri için parantez dengesi kontrolü

            var commonExtensions = new[] { ".js", ".ts", ".java", ".php", ".go", ".cpp", ".c", ".h", ".sql", ".json", ".xml" };

            if (commonExtensions.Contains(ext))

            {

                var lines = content.Split('\n');

                foreach (var line in lines)

                {

                    var trimmed = line.Trim();

                    int openParen = 0, openBracket = 0, openBrace = 0;

                    foreach (char c in trimmed)

                    {

                        if (c == '(') openParen++;

                        if (c == ')') openParen--;

                        if (c == '[') openBracket++;

                        if (c == ']') openBracket--;

                        if (c == '{') openBrace++;

                        if (c == '}') openBrace--;

                    }

                    if (openParen != 0 || openBracket != 0 || openBrace != 0)

                        return true;

                }

            }

            return false;

        }

        catch

        {

            return false;

        }

    }

    // TreeView'deki dosya öğesinin rengini günceller

    private void UpdateTreeViewItemErrorState(string filePath, bool hasError)

    {

        if (!Dispatcher.CheckAccess())

        {

            Dispatcher.Invoke(() => UpdateTreeViewItemErrorState(filePath, hasError));

            return;

        }

        // TreeView'deki dosya öğesini bul

        TreeViewItem? targetItem = FindTreeViewItemByPath(tvFileTree, filePath);

        if (targetItem != null)

        {

            targetItem.Foreground = hasError ? Brushes.Red : (SolidColorBrush)FindResource("DarkText");

        }

        // Hata durumunu kaydet

        _fileErrorStates[filePath] = hasError;

    }

    private void ApplyDiagnosticsToEditor(TextEditor editor, string filePath, string content)

    {

        if (!Dispatcher.CheckAccess())

        {

            Dispatcher.Invoke(() => ApplyDiagnosticsToEditor(editor, filePath, content));

            return;

        }

        // Get diagnostics from both sources: PluginManager and LSP (_fileDiagnostics)

        var pluginDiagnostics = PluginManager.GetDiagnostics(filePath, content);

        var allDiagnostics = new List<LanguageDiagnostic>(pluginDiagnostics);

        // Add LSP diagnostics if available

        if (_lspDiagnostics.TryGetValue(filePath, out var lspDiagnostics))

        {

            allDiagnostics.AddRange(lspDiagnostics);

        }

        _fileDiagnostics[filePath] = allDiagnostics;

        UpdateTreeViewItemErrorState(filePath, allDiagnostics.Count > 0);

        if (editor.Tag is TabItem diagTabItem && diagTabItem.Tag is TabInfo diagTabInfo && diagTabInfo.OverviewMargin != null)
        {
            diagTabInfo.OverviewMargin.SetDiagnostics(allDiagnostics);
        }

        // Remove old diagnostic renderers

        var existingRenderers = editor.TextArea.TextView.BackgroundRenderers.OfType<DiagnosticLineBackgroundRenderer>().ToList();

        foreach (var renderer in existingRenderers)

        {

            editor.TextArea.TextView.BackgroundRenderers.Remove(renderer);

        }

        if (allDiagnostics.Count == 0)

        {

            editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);

            return;

        }

        foreach (var diagnostic in allDiagnostics)

        {

            if (diagnostic.Line < 1 || diagnostic.Line > editor.Document.LineCount)

                continue;

            editor.TextArea.TextView.BackgroundRenderers.Add(

                new DiagnosticLineBackgroundRenderer(diagnostic.Line, new SolidColorBrush(Color.FromArgb(40, 255, 0, 0))));

        }

        editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);

    }

    private void LstDiagnostics_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement element && element.DataContext is DiagnosticListItem item && File.Exists(item.FilePath))
            OpenFileAndHighlight(item.FilePath, item.LineNumber, string.Empty);
    }

    private sealed class DiagnosticListItem
    {
        public string FilePath { get; init; } = "";
        public int LineNumber { get; init; }
        public string Message { get; init; } = "";
        public DiagnosticSource Source { get; init; } = DiagnosticSource.Plugin;
        public string SourceBadge => Source == DiagnosticSource.Lsp ? "[LSP]" : "[Plugin]";
        public string DisplayText => $"{Path.GetFileName(FilePath)}:{LineNumber} {SourceBadge} - {Message}";
    }

    private class DiagnosticLineBackgroundRenderer : IBackgroundRenderer

    {

        private readonly int _lineNumber;

        private readonly Brush _background;

        public DiagnosticLineBackgroundRenderer(int lineNumber, Brush background)

        {

            _lineNumber = lineNumber;

            _background = background;

        }

        public KnownLayer Layer => KnownLayer.Selection;

        public void Draw(TextView textView, DrawingContext drawingContext)

        {

            if (textView.Document == null || _lineNumber < 1 || _lineNumber > textView.Document.LineCount)

                return;

            var line = textView.Document.GetLineByNumber(_lineNumber);

            if (line == null)

                return;

            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, line))

            {

                var drawRect = new Rect(rect.Location, new Size(textView.ActualWidth, rect.Height));

                drawingContext.DrawRectangle(_background, null, drawRect);

            }

        }

    }

    // TreeView'de belirtilen yola sahip öğeyi bulur (özyinelemeli)

    private TreeViewItem? FindTreeViewItemByPath(ItemsControl parent, string targetPath)

    {

        foreach (var item in parent.Items)

        {

            if (item is TreeViewItem treeItem)

            {

                if (treeItem.Tag is string path &&

                    Path.GetFullPath(path).Equals(Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))

                {

                    return treeItem;

                }

                // Alt öğeleri kontrol et

                var childResult = FindTreeViewItemByPath(treeItem, targetPath);

                if (childResult != null)

                {

                    return childResult;

                }

            }

            RefreshTelemetryPanel();

        }

        return null;

    }

    private async Task ScanProjectDiagnosticsAsync(string folderPath)

    {

        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))

            return;

        try

        {

            var supportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)

            {

                ".cs", ".py", ".js", ".ts", ".jsx", ".tsx", ".java", ".php", ".go", ".c", ".cpp", ".cxx", ".cc", ".h", ".hpp", ".sql", ".html", ".htm", ".css", ".scss", ".sass", ".json", ".xml", ".rb", ".rs", ".kt", ".swift"

            };

            var files = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)

                .Where(f => supportedExtensions.Contains(Path.GetExtension(f)))

                .Where(f => !IsIgnoredPath(f))

                .ToList();

            foreach (var file in files)

            {

                try

                {

                    var content = await File.ReadAllTextAsync(file);

                    var diagnostics = PluginManager.GetDiagnostics(file, content);

                    var hasError = diagnostics.Count > 0;

                    _fileDiagnostics[file] = diagnostics;

                    _fileErrorStates[file] = hasError;

                    UpdateTreeViewItemErrorState(file, hasError);

                }

                catch

                {

                    // Bazı dosyalar okunamazsa atla

                }

            }

        }

        catch (Exception ex)

        {

            AddTerminalMessage($"Proje taraması sırasında hata oluştu: {ex.Message}");

        }

    }

    private bool IsIgnoredPath(string filePath)

    {

        var ignoredFolders = new[] { ".git", "node_modules", "bin", "obj", ".vs", "packages" };

        var directory = Path.GetDirectoryName(filePath);

        if (directory == null)

            return false;

        var parts = directory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return parts.Any(part => ignoredFolders.Contains(part, StringComparer.OrdinalIgnoreCase));

    }

    private void BtnCloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement closeElement && closeElement.Tag is TabItem tabItem && tabItem.Tag is TabInfo tabInfo)
        {

            if (tabInfo.IsModified)

            {

                var result = MessageBox.Show(

                    $"\"{Path.GetFileName(tabInfo.FilePath)}\" dosyasında kaydedilmemiş değişiklikler var. Kaydetmek istiyor musunuz?",

                    "Kaydedilmemiş Değişiklikler",

                    MessageBoxButton.YesNoCancel,

                    MessageBoxImage.Warning

                );

                if (result == MessageBoxResult.Yes)

                {

                    SaveTab(tabItem);

                }

                else if (result == MessageBoxResult.Cancel)

                {

                    return;

                }

            }

            tcEditor.Items.Remove(tabItem);

        }

    }

    private void SaveTab(TabItem tabItem)

    {

        if (tabItem.Tag is TabInfo tabInfo && tabItem.Content is TextEditor editor)

        {

            try

            {

                if (tabInfo.IsUntitled)

                {

                    var saveDialog = new SaveFileDialog

                    {

                        Title = LocalizationManager.Instance.GetString("KodTaslaginiKaydet"),

                        FileName = string.IsNullOrWhiteSpace(tabInfo.DisplayName) ? "ai-snippet.txt" : tabInfo.DisplayName.Replace(" ", "-") + ".txt",

                        Filter = "Tüm dosyalar|*.*|Metin dosyaları|*.txt"

                    };

                    if (saveDialog.ShowDialog(this) != true)

                    {

                        return;

                    }

                    tabInfo.FilePath = saveDialog.FileName;

                    tabInfo.DisplayName = Path.GetFileName(tabInfo.FilePath);

                    tabInfo.IsUntitled = false;

                    UpdateTabHeader(tabItem, tabInfo);

                }

                editor.Save(tabInfo.FilePath);

                ResetTabHeaderModifiedState(tabItem, tabInfo);

                AddTerminalMessage($"Dosya kaydedildi: {tabInfo.FilePath}");

                Notify($"Dosya kaydedildi: {Path.GetFileName(tabInfo.FilePath)}", NotificationSeverity.Success);

            }

            catch (Exception ex)

            {

                AddTerminalMessage($"Dosya kaydedilirken hata oluştu: {ex.Message}");

                Notify($"Dosya kaydedilemedi: {ex.Message}", NotificationSeverity.Error);

                MessageBox.Show(LocalizationManager.Instance.GetString("DosyaKaydedilirkenHataOlustuExMessage").Replace("{ex.Message}", ex.Message), LocalizationManager.Instance.GetString("Hata"), MessageBoxButton.OK, MessageBoxImage.Error);

            }

        }

    }

    private void BtnSaveFile_Click(object sender, RoutedEventArgs e)

    {

        SaveCurrentFile();

    }

    private void BtnSaveAll_Click(object sender, RoutedEventArgs e)

    {

        SaveAllTabs();

    }

    private void SaveAllTabs()

    {

        foreach (TabItem tabItem in tcEditor.Items)

        {

            if (tabItem.Tag is TabInfo tabInfo && tabItem.Content is TextEditor)

            {

                SaveTab(tabItem);

            }

        }

        AddTerminalMessage(LocalizationManager.Instance.GetString("TumAcikSekmelerKaydedildi"));

        Notify("Tüm açık sekmeler kaydedildi.", NotificationSeverity.Success);

    }

    private void TxtCodeEditor_PreviewKeyDown(object sender, KeyEventArgs e)

    {

        if (completionPopup.IsOpen)

        {

            if (e.Key == Key.Down)

            {

                _selectedCompletionIndex = Math.Min(_selectedCompletionIndex + 1, _currentCompletionItems.Count - 1);

                completionListBox.SelectedIndex = _selectedCompletionIndex;

                completionListBox.ScrollIntoView(completionListBox.SelectedItem);

                e.Handled = true;

            }

            else if (e.Key == Key.Up)

            {

                _selectedCompletionIndex = Math.Max(_selectedCompletionIndex - 1, 0);

                completionListBox.SelectedIndex = _selectedCompletionIndex;

                completionListBox.ScrollIntoView(completionListBox.SelectedItem);

                e.Handled = true;

            }

            else if (e.Key == Key.Enter || e.Key == Key.Tab)

            {

                InsertSelectedCompletion();

                e.Handled = true;

            }

            else if (e.Key == Key.Escape)

            {

                completionPopup.IsOpen = false;

                e.Handled = true;

            }

            return;

        }

        if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)

        {

            SaveCurrentFile();

            e.Handled = true;

        }

        else if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)

        {

            // Ctrl+F: AvalonEdit SearchPanel zaten Ctrl+F'i dinler; bu satır

            // odak dışındaki durumlarda paneli açmak ve otomatik odaklanmak için güvence sağlar.

            if (sender is TextEditor ed)

            {

                var panel = SearchPanel.Install(ed); // zaten kuruluysa mevcut örneği döner

                ApplySearchPanelStyle(panel);

                panel.Open();

                panel.Reactivate();

                FocusSearchPanelTextBox(panel);

            }

            else if (tcEditor.SelectedItem is TabItem activeTab && activeTab.Content is TextEditor activeEd)

            {

                var panel = SearchPanel.Install(activeEd);

                ApplySearchPanelStyle(panel);

                panel.Open();

                panel.Reactivate();

                FocusSearchPanelTextBox(panel);

            }

            e.Handled = true;

        }

        else if (e.Key == Key.B && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)

        {

            ToggleLeftPanel();

            e.Handled = true;

        }

        else if (e.Key == Key.L && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)

        {

            ToggleRightPanel();

            e.Handled = true;

        }


        else if (e.Key == Key.F12 && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            RunBackground(FindReferencesAsync(sender as TextEditor), "FindReferences");
            e.Handled = true;
        }

        else if (e.Key == Key.F12 && Keyboard.Modifiers == ModifierKeys.None)
        {
            RunBackground(GoToDefinitionAsync(sender as TextEditor), "GoToDefinition");
            e.Handled = true;
        }

        else if (e.Key == Key.T && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            RunBackground(ShowDocumentSymbolsAsync(sender as TextEditor), "DocumentSymbols");
            e.Handled = true;
        }

        else if (e.Key == Key.Space && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)

        {

            RunBackground(ShowCompletionAsync(sender as TextEditor), "ShowCompletion");

            e.Handled = true;

        }

    }

    private async System.Threading.Tasks.Task ShowCompletionAsync(TextEditor? editor)

    {

        if (editor == null || tcEditor.SelectedItem is not TabItem activeTab || activeTab.Tag is not TabInfo tabInfo)

            return;

        var filePath = tabInfo.FilePath;

        var caretOffset = editor.CaretOffset;

        var location = editor.Document.GetLocation(caretOffset);

        // LSP'den completions al

        var languageExtension = GetLspLanguageExtension(filePath);
        if (string.IsNullOrEmpty(languageExtension) || !_languageServerServices.TryGetValue(languageExtension, out var languageServer))
            return;

        _currentCompletionItems = await languageServer.GetCompletionsAsync(filePath, location.Line - 1, location.Column - 1);

        if (_currentCompletionItems.Count == 0)

            {

                completionPopup.IsOpen = false;

                return;

            }

        completionListBox.ItemsSource = _currentCompletionItems;

        completionListBox.SelectedIndex = 0;

        // Popup pozisyonunu ayarla

        var textView = editor.TextArea.TextView;

        if (textView.VisualLinesValid)

        {

            var visualLine = textView.GetVisualLine(location.Line);

            if (visualLine != null)

            {

                var visualColumn = visualLine.GetVisualColumn(location.Column - 1);

                var position = visualLine.GetVisualPosition(visualColumn, ICSharpCode.AvalonEdit.Rendering.VisualYPosition.TextTop);

                var point = textView.PointToScreen(position);

                completionPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Absolute;

                completionPopup.HorizontalOffset = point.X;

                completionPopup.VerticalOffset = point.Y + 15;

                completionPopup.IsOpen = true;

            }

            else

            {

                completionPopup.IsOpen = false;

            }

        }

        else

        {

            completionPopup.IsOpen = false;

        }

    }

    private async Task GoToDefinitionAsync(TextEditor? editor)
    {
        if (editor == null || tcEditor.SelectedItem is not TabItem activeTab || activeTab.Tag is not TabInfo tabInfo)
            return;

        var location = editor.Document.GetLocation(editor.CaretOffset);
        var languageExtension = GetLspLanguageExtension(tabInfo.FilePath);
        if (string.IsNullOrEmpty(languageExtension) || !_languageServerServices.TryGetValue(languageExtension, out var languageServer))
            return;

        var definitions = await languageServer.GetDefinitionsAsync(
            tabInfo.FilePath,
            location.Line - 1,
            location.Column - 1);

        var definition = definitions.FirstOrDefault();
        if (definition == null)
        {
            Notify("Tanım bulunamadı.", NotificationSeverity.Info);
            return;
        }

        OpenFileAndHighlight(definition.FilePath, definition.Line, string.Empty);
    }

    private async Task FindReferencesAsync(TextEditor? editor)
    {
        if (editor == null || tcEditor.SelectedItem is not TabItem activeTab || activeTab.Tag is not TabInfo tabInfo)
            return;

        var location = editor.Document.GetLocation(editor.CaretOffset);
        var languageExtension = GetLspLanguageExtension(tabInfo.FilePath);
        if (string.IsNullOrEmpty(languageExtension) || !_languageServerServices.TryGetValue(languageExtension, out var languageServer))
            return;

        var references = await languageServer.GetReferencesAsync(tabInfo.FilePath, location.Line - 1, location.Column - 1);
        var results = references.Select(reference => new SearchResultItem
        {
            FilePath = reference.FilePath,
            LineNumber = reference.Line,
            LineText = "LSP reference",
            SearchQuery = string.Empty
        }).ToList();

        ShowLanguageResults(results, results.Count == 0 ? "❌ Referans bulunamadı." : "🔎 Referanslar");
    }

    private async Task ShowDocumentSymbolsAsync(TextEditor? editor)
    {
        if (editor == null || tcEditor.SelectedItem is not TabItem activeTab || activeTab.Tag is not TabInfo tabInfo)
            return;

        var languageExtension = GetLspLanguageExtension(tabInfo.FilePath);
        if (string.IsNullOrEmpty(languageExtension) || !_languageServerServices.TryGetValue(languageExtension, out var languageServer))
            return;

        var symbols = await languageServer.GetDocumentSymbolsAsync(tabInfo.FilePath);
        var results = symbols.Select(symbol => new SearchResultItem
        {
            FilePath = symbol.Location.FilePath,
            LineNumber = symbol.Location.Line,
            LineText = symbol.Name,
            SearchQuery = string.Empty
        }).ToList();

        ShowLanguageResults(results, results.Count == 0 ? "❌ Sembol bulunamadı." : "🔹 Semboller");
    }

    private void ShowLanguageResults(List<SearchResultItem> results, string emptyMessage)
    {
        tvFileTree.Visibility = Visibility.Collapsed;
        lstSearchResultsBorder.Visibility = Visibility.Visible;
        lstSearchResults.ItemsSource = results.Count == 0
            ? new List<SearchResultItem> { new SearchResultItem { CustomMessage = emptyMessage } }
            : results;
    }

    private void InsertSelectedCompletion()

    {

        if (completionListBox.SelectedItem is CompletionItem item && tcEditor.SelectedItem is TabItem activeTab && activeTab.Content is TextEditor editor)

        {

            // Basit bir şekilde insertText'i ekle (ileride prefix'i kaldırmak için iyileştirilebilir)

            editor.Document.Insert(editor.CaretOffset, item.InsertText);

            completionPopup.IsOpen = false;

        }

    }

    private void CompletionListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)

    {

        if (completionListBox.SelectedIndex >= 0)

            _selectedCompletionIndex = completionListBox.SelectedIndex;

    }

    private void CompletionListBox_KeyDown(object sender, KeyEventArgs e)

    {

        if (e.Key == Key.Enter || e.Key == Key.Tab)

        {

            InsertSelectedCompletion();

            e.Handled = true;

        }

    }

    private void SaveCurrentFile()

    {

        if (tcEditor.SelectedItem is TabItem activeTab)

        {

            SaveTab(activeTab);

        }

        else

        {

            AddTerminalMessage("Kaydedilecek aktif sekme yok.");

            Notify("Kaydedilecek aktif sekme yok.", NotificationSeverity.Info);

        }

    }

    private void ReloadTabIfOpen(string filePath)

    {

        if (!Dispatcher.CheckAccess())

        {

            Dispatcher.Invoke(() => ReloadTabIfOpen(filePath));

            return;

        }

        try

        {

            var fullPath = Path.GetFullPath(filePath);

            foreach (TabItem tabItem in tcEditor.Items)

            {

                if (tabItem.Tag is TabInfo tabInfo && Path.GetFullPath(tabInfo.FilePath).Equals(fullPath, StringComparison.OrdinalIgnoreCase))

                {

                    if (tabItem.Content is TextEditor editor)

                    {

                        editor.Load(fullPath);

                        ResetTabHeaderModifiedState(tabItem, tabInfo);

                        AddTerminalMessage($"Sekme AI tarafından güncellendi ve yeniden yüklendi: {Path.GetFileName(fullPath)}");

                    }

                    break;

                }

            }

        }

        catch (Exception ex)

        {

            AddTerminalMessage($"Sekme canlı güncelleme hatası: {ex.Message}");

        }

    }

    private void UpdateBreadcrumb(string filePath)

    {

        if (string.IsNullOrEmpty(_selectedFolder))

        {

            var title = $"Yengi - {Path.GetFileName(filePath)}";

            tbCurrentFile.Text = $"📄 {Path.GetFileName(filePath)}";

            this.Title = title;

            return;

        }

        try

        {

            var relativePath = Path.GetRelativePath(_selectedFolder, filePath);

            var projectFolderName = Path.GetFileName(_selectedFolder);

            if (string.IsNullOrEmpty(projectFolderName))

            {

                projectFolderName = _selectedFolder;

            }

            var breadcrumb = projectFolderName + " > " + relativePath.Replace(Path.DirectorySeparatorChar.ToString(), " > ");

            tbCurrentFile.Text = $"📄 {breadcrumb}";

            this.Title = $"Yengi - {breadcrumb}";

        }

        catch

        {

            tbCurrentFile.Text = $"📄 {Path.GetFileName(filePath)}";

            this.Title = $"Yengi - {Path.GetFileName(filePath)}";

        }

    }

    private void TcEditor_SelectionChanged(object sender, SelectionChangedEventArgs e)

    {

        if (e.Source == tcEditor)

        {

            if (tcEditor.SelectedItem is TabItem activeTab && activeTab.Tag is TabInfo tabInfo)

            {

                _currentOpenFile = tabInfo.FilePath;

                UpdateBreadcrumb(_currentOpenFile);

            }

            else

            {

                _currentOpenFile = null;

                tbCurrentFile.Text = "📄 Dosya Açık Değil";

            }

        }

        // Sekme değişince LSP durum badge'ini güncelle
        if (e.Source == tcEditor)
        {
            if (tcEditor.SelectedItem is TabItem activeTab2 && activeTab2.Tag is TabInfo tabInfo2)
                UpdateLspStatusBar(tabInfo2.FilePath);
            else
                UpdateLspStatusBar(null);
        }

    }

    private System.Windows.Threading.DispatcherTimer? _searchDebounceTimer;

    private void TxtSearchFiles_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_searchDebounceTimer == null)
        {
            _searchDebounceTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(350)
            };
            _searchDebounceTimer.Tick += (s, args) =>
            {
                _searchDebounceTimer.Stop();
                RunBackground(PerformSearchAsync(), "PerformSearch");
            };
        }

        _searchDebounceTimer.Stop();

        var query = txtSearchFiles.Text.Trim();
        if (string.IsNullOrEmpty(query))
        {
            lstSearchResultsBorder.Visibility = Visibility.Collapsed;
            tvFileTree.Visibility = Visibility.Visible;
            return;
        }

        // 3 veya daha fazla karakter girilince yazma durduktan 350ms sonra otomatik ara
        if (query.Length >= 3)
        {
            _searchDebounceTimer.Start();
        }
    }

    private void TxtSearchFiles_KeyDown(object sender, KeyEventArgs e)

    {

        if (e.Key == Key.Enter)

        {

            _searchDebounceTimer?.Stop();

            RunBackground(PerformSearchAsync(), "PerformSearch");

        }

    }

    private void BtnSearchFiles_Click(object sender, RoutedEventArgs e)

    {

        RunBackground(PerformSearchAsync(), "PerformSearch");

    }

    private void BtnClearSearch_Click(object sender, RoutedEventArgs e)

    {

        txtSearchFiles.Clear();

        lstSearchResultsBorder.Visibility = Visibility.Collapsed;

        tvFileTree.Visibility = Visibility.Visible;

    }

    private async Task PerformSearchAsync()

    {

        var query = txtSearchFiles.Text.Trim();

        if (string.IsNullOrEmpty(query))

        {

            lstSearchResultsBorder.Visibility = Visibility.Collapsed;

            tvFileTree.Visibility = Visibility.Visible;

            return;

        }

        if (string.IsNullOrEmpty(_selectedFolder) || !Directory.Exists(_selectedFolder))

        {

            MessageBox.Show(LocalizationManager.Instance.GetString("LutfenOnceBirProjeKlasoruSecin"), LocalizationManager.Instance.GetString("AramaYapilamiyor"), MessageBoxButton.OK, MessageBoxImage.Warning);

            return;

        }

        tvFileTree.Visibility = Visibility.Collapsed;

        lstSearchResultsBorder.Visibility = Visibility.Visible;

        // Arama yapılıyor belirteci

        lstSearchResults.ItemsSource = new List<SearchResultItem> { new SearchResultItem { CustomMessage = "🔍 Aranıyor..." } };

        try

        {

            var results = await Task.Run(() => SearchInFiles(_selectedFolder, query));

            if (results.Count == 0)

            {

                lstSearchResults.ItemsSource = new List<SearchResultItem> { new SearchResultItem { CustomMessage = "❌ Sonuç bulunamadı." } };

            }

            else

            {

                lstSearchResults.ItemsSource = results;

            }

        }

        catch (Exception ex)

        {

            AddTerminalMessage($"Arama hatası: {ex.Message}");

            lstSearchResults.ItemsSource = new List<SearchResultItem> { new SearchResultItem { CustomMessage = $"❌ Arama hatası: {ex.Message}" } };

        }

    }

    private List<SearchResultItem> SearchInFiles(string directory, string query)

    {

        var results = new List<SearchResultItem>();

        // Atlanacak klasör listesi

        var skipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)

        {

            ".git", "node_modules", "bin", "obj", ".vs", ".mdai", "backup", ".plugin_symlinks"

        };

        // Binary/görsel uzantıları atla

        var skipExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)

        {

            ".png", ".jpg", ".jpeg", ".gif", ".ico", ".exe", ".dll", ".zip", 

            ".rar", ".pdf", ".mp3", ".mp4", ".wav", ".avi", ".pdb", ".suo", ".user"

        };

        SearchInFilesRecursive(directory, query, skipDirs, skipExtensions, results);

        return results;

    }

    private static bool IsBinaryFile(string filePath)

    {

        // İlk 1000 byte'ı oku ve binary olup olmadığını kontrol et

        try

        {

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            var buffer = new byte[Math.Min(1000, stream.Length)];

            int bytesRead = stream.Read(buffer, 0, buffer.Length);

            // Eğer null byte (0x00) varsa, büyük ihtimal binary dosyadır

            // Ayrıca, toplam byte'ların %30'ünden fazlası kontrol edilemeyen karakterse (0x00-0x08, 0x0E-0x1F, 0x7F-0x9F), binary kabul et

            int nullCount = 0;

            int nonPrintableCount = 0;

            for (int i = 0; i < bytesRead; i++)

            {

                byte b = buffer[i];

                if (b == 0x00)

                {

                    nullCount++;

                }

                if ((b < 0x09) || (b > 0x0D && b < 0x20) || (b > 0x7E && b < 0xA0))

                {

                    nonPrintableCount++;

                }

            }

            if (nullCount > 0)

                return true;

            if (bytesRead > 0 && (double)nonPrintableCount / bytesRead > 0.30)

                return true;

            return false;

        }

        catch

        {

            // Eğer dosya okunamazsa, güvenli tarafta olmak için binary kabul et

            return true;

        }

    }

    private string? GetOpenFileTextContent(string filePath)
    {
        string? result = null;
        Dispatcher.Invoke(() =>
        {
            try
            {
                var fullPath = Path.GetFullPath(filePath);
                foreach (TabItem item in tcEditor.Items)
                {
                    if (item.Tag is TabInfo info && Path.GetFullPath(info.FilePath).Equals(fullPath, StringComparison.OrdinalIgnoreCase) && item.Content is TextEditor editor)
                    {
                        result = editor.Text;
                        break;
                    }
                }
            }
            catch { }
        });
        return result;
    }

    private void SearchInFilesRecursive(string directory, string query, HashSet<string> skipDirs, HashSet<string> skipExtensions, List<SearchResultItem> results)

    {

        if (results.Count > 1000)

            return;

        try

        {

            // Önce bu dizindeki dosyaları işle

            foreach (var file in Directory.EnumerateFiles(directory, "*.*", SearchOption.TopDirectoryOnly))

            {

                if (results.Count > 1000)

                    return;

                var ext = Path.GetExtension(file);

                if (skipExtensions.Contains(ext))

                    continue;

                if (IsBinaryFile(file))

                    continue;

                // Dosya içeriğini ve adını ara

                try

                {

                    var fileName = Path.GetFileName(file);
                    if (fileName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        results.Add(new SearchResultItem
                        {
                            FilePath = file,
                            LineNumber = 1,
                            LineText = $"📄 {fileName}",
                            SearchQuery = query
                        });
                    }

                    string content;
                    var inMemoryText = GetOpenFileTextContent(file);
                    if (inMemoryText != null)
                    {
                        content = inMemoryText;
                    }
                    else
                    {
                        content = File.ReadAllText(file, System.Text.Encoding.UTF8);
                    }

                    var lines = content.Split('\n');
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var line = lines[i];
                        if (line.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            results.Add(new SearchResultItem
                            {
                                FilePath = file,
                                LineNumber = i + 1,
                                LineText = line.TrimEnd('\r'),
                                SearchQuery = query
                            });

                            if (results.Count > 1000) break;
                        }
                    }

                }

                catch

                {

                    // Bazı dosyalar kilitli veya okunamıyor olabilir, sessizce geç

                }

            }

            // Sonra alt dizinleri işle (sembolik bağlantı değilse)

            foreach (var subDir in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))

            {

                if (results.Count > 1000)

                    return;

                var dirName = Path.GetFileName(subDir);

                if (skipDirs.Contains(dirName))

                    continue;

                // Sembolik bağlantı (symlink/junction) atla

                try

                {

                    var dirInfo = new DirectoryInfo(subDir);

                    if ((dirInfo.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)

                        continue;

                }

                catch

                {

                    continue;

                }

                // Alt dizini rekürsif olarak işle

                try

                {

                    SearchInFilesRecursive(subDir, query, skipDirs, skipExtensions, results);

                }

                catch

                {

                    // Alt dizin okunurken hata olursa sessizce geç

                }

            }

        }

        catch

        {

            // Dizin okunurken hata olursa sessizce geç

        }

    }

    private void LstSearchResults_MouseDoubleClick(object sender, MouseButtonEventArgs e)

    {

        // ItemsControl'de SelectedItem yok; tıklanan DataContext'ten alıyoruz

        if (e.OriginalSource is FrameworkElement fe && fe.DataContext is SearchResultItem item)

        {

            if (!string.IsNullOrEmpty(item.FilePath) && File.Exists(item.FilePath))

            {

                OpenFileAndHighlight(item.FilePath, item.LineNumber, item.SearchQuery);

            }

        }

    }

    private void OpenFileAndHighlight(string filePath, int lineNumber, string query)

    {

        try

        {

            // Önce dosyayı normal olarak aç

            OpenFile(filePath);

            // Açılan sekmeyi bul ve editörünü al

            var fullPath = Path.GetFullPath(filePath);

            foreach (TabItem tabItem in tcEditor.Items)

            {

                if (tabItem.Tag is TabInfo tabInfo && Path.GetFullPath(tabInfo.FilePath).Equals(fullPath, StringComparison.OrdinalIgnoreCase))

                {

                    tcEditor.SelectedItem = tabItem;

                    if (tabItem.Content is TextEditor editor)

                    {

                        // UI'ın render edilip belgenin yüklenmesi için Dispatcher ile çalıştırıyoruz

                        _ = Dispatcher.BeginInvoke(new Action(() =>

                        {

                            try

                            {

                                if (lineNumber <= editor.Document.LineCount)

                                {

                                    var line = editor.Document.GetLineByNumber(lineNumber);

                                    editor.ScrollToLine(lineNumber);

                                    // Kelimenin satırdaki konumunu bul

                                    var lineText = editor.Document.GetText(line.Offset, line.Length);

                                    var idx = lineText.IndexOf(query, StringComparison.OrdinalIgnoreCase);

                                    if (idx >= 0)

                                    {

                                        editor.Select(line.Offset + idx, query.Length);

                                    }

                                    else

                                    {

                                        // Kelime bulunamadıysa satırın kendisini seç

                                        editor.Select(line.Offset, line.Length);

                                    }

                                    editor.Focus();

                                }

                            }

                            catch (Exception ex)

                            {

                                AddTerminalMessage($"Satır vurgulama hatası: {ex.Message}");

                            }

                        }), System.Windows.Threading.DispatcherPriority.Loaded);

                    }

                    break;

                }

            }

        }

        catch (Exception ex)

        {

            AddTerminalMessage($"Dosya arama konumuna gidilemedi: {ex.Message}");

        }

    }

    private void ApplySearchPanelStyle(ICSharpCode.AvalonEdit.Search.SearchPanel panel)

    {

        if (panel == null) return;

        panel.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1a2433"));

        panel.Foreground = Brushes.White;

        panel.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2d3a4f"));

        panel.BorderThickness = new Thickness(1);

        // Arama eşleşmelerini açık yeşil yerine VS Code tarzı şık ve yüksek kontrastlı kehribar/turuncu yap
        panel.MarkerBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#854d0e"));

        var textBoxStyle = new Style(typeof(TextBox));

        textBoxStyle.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0f172a"))));

        textBoxStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));

        textBoxStyle.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"))));

        textBoxStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));

        textBoxStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 2, 6, 2)));

        textBoxStyle.Setters.Add(new Setter(TextBox.CaretBrushProperty, Brushes.White));

        var buttonStyle = new Style(typeof(Button));

        buttonStyle.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#253246"))));

        buttonStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));

        buttonStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));

        buttonStyle.Setters.Add(new Setter(Control.MarginProperty, new Thickness(2)));

        buttonStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 3, 6, 3)));

        panel.Resources[typeof(TextBox)] = textBoxStyle;

        panel.Resources[typeof(Button)] = buttonStyle;

        panel.Loaded -= SearchPanel_Loaded;

        panel.Loaded += SearchPanel_Loaded;

        CustomizeSearchPanelTemplate(panel);

    }

    private void SearchPanel_Loaded(object sender, RoutedEventArgs e)

    {

        if (sender is ICSharpCode.AvalonEdit.Search.SearchPanel panel)

        {

            CustomizeSearchPanelTemplate(panel);

        }

    }

    private void FocusSearchPanelTextBox(ICSharpCode.AvalonEdit.Search.SearchPanel panel)

    {

        if (panel == null) return;

        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>

        {

            try

            {

                var searchBox = panel.Template.FindName("PART_searchTextBox", panel) as TextBox;

                if (searchBox != null)

                {

                    searchBox.Focus();

                    Keyboard.Focus(searchBox);

                    searchBox.SelectAll();

                }

            }

            catch { }

        }));

    }

    private void CustomizeSearchPanelTemplate(ICSharpCode.AvalonEdit.Search.SearchPanel panel)

    {

        try

        {

            var prevBtn = panel.Template.FindName("PART_FindPreviousButton", panel) as Button;

            if (prevBtn != null)

            {

                prevBtn.Content = "▲";

                prevBtn.ToolTip = "Önceki Eşleşme (Shift+F3)";

            }

            var nextBtn = panel.Template.FindName("PART_FindNextButton", panel) as Button;

            if (nextBtn != null)

            {

                nextBtn.Content = "▼";

                nextBtn.ToolTip = "Sonraki Eşleşme (F3)";

            }

            var closeBtn = panel.Template.FindName("PART_CloseButton", panel) as Button;

            if (closeBtn != null)

            {

                closeBtn.Content = "✕";

                closeBtn.ToolTip = "Kapat (Esc)";

                closeBtn.Click += (s, e) =>
                {
                    if (tcEditor.SelectedItem is TabItem activeTab && activeTab.Tag is TabInfo tabInfo && tabInfo.OverviewMargin != null)
                    {
                        tabInfo.OverviewMargin.SetSearchMatches(null);
                    }
                };

            }

            var searchBox = panel.Template.FindName("PART_searchTextBox", panel) as TextBox;
            if (searchBox != null)
            {
                searchBox.DataContext = panel;
                searchBox.TextChanged -= SearchBox_TextChanged_OverviewMargin;
                searchBox.TextChanged += SearchBox_TextChanged_OverviewMargin;
                UpdateSearchOverviewMatches(panel, searchBox.Text);
            }

            FocusSearchPanelTextBox(panel);

        }

        catch { }

    }

    private void SearchBox_TextChanged_OverviewMargin(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            var panel = tb.DataContext as ICSharpCode.AvalonEdit.Search.SearchPanel;
            if (panel != null)
            {
                UpdateSearchOverviewMatches(panel, tb.Text);
            }
            else if (tcEditor.SelectedItem is TabItem activeTab && activeTab.Content is TextEditor ed)
            {
                var p = ICSharpCode.AvalonEdit.Search.SearchPanel.Install(ed);
                UpdateSearchOverviewMatches(p, tb.Text);
            }
        }
    }

    private void UpdateSearchOverviewMatches(ICSharpCode.AvalonEdit.Search.SearchPanel panel, string searchText)
    {
        if (tcEditor.SelectedItem is TabItem activeTab && activeTab.Tag is TabInfo tabInfo && tabInfo.OverviewMargin != null)
        {
            if (string.IsNullOrWhiteSpace(searchText) || activeTab.Content is not TextEditor editor || editor.Document == null)
            {
                tabInfo.OverviewMargin.SetSearchMatches(null);
                return;
            }

            var matchingLines = new List<int>();
            var doc = editor.Document;
            int lineCount = doc.LineCount;
            var mode = panel.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            for (int i = 1; i <= lineCount; i++)
            {
                var line = doc.GetLineByNumber(i);
                string lineText = doc.GetText(line.Offset, line.Length);
                if (lineText.IndexOf(searchText, mode) >= 0)
                {
                    matchingLines.Add(i);
                }
            }

            tabInfo.OverviewMargin.SetSearchMatches(matchingLines);
        }
    }



    private GridLength _savedLeftPanelWidth = new GridLength(250);

    private GridLength _savedRightPanelWidth = new GridLength(450);



    private void BtnToggleLeftPanel_Click(object sender, RoutedEventArgs e)

    {

        ToggleLeftPanel();

    }



    private void BtnToggleRightPanel_Click(object sender, RoutedEventArgs e)

    {

        ToggleRightPanel();

    }



    public void ToggleLeftPanel()

    {

        if (colLeftPanel == null) return;

        if (colLeftPanel.Width.Value > 0)

        {

            _savedLeftPanelWidth = colLeftPanel.Width;

            colLeftPanel.MinWidth = 0;

            colLeftPanel.Width = new GridLength(0);

            if (colLeftSplitter != null) colLeftSplitter.Width = new GridLength(0);

        }

        else

        {

            colLeftPanel.MinWidth = 180;

            colLeftPanel.Width = _savedLeftPanelWidth.Value >= 180 ? _savedLeftPanelWidth : new GridLength(250);

            if (colLeftSplitter != null) colLeftSplitter.Width = new GridLength(5);

        }

    }



    public void ToggleRightPanel()

    {

        if (colRightPanel == null) return;

        if (colRightPanel.Width.Value > 0)

        {

            _savedRightPanelWidth = colRightPanel.Width;

            colRightPanel.MinWidth = 0;

            colRightPanel.Width = new GridLength(0);

            if (colRightSplitter != null) colRightSplitter.Width = new GridLength(0);

        }

        else

        {

            colRightPanel.MinWidth = 260;

            colRightPanel.Width = _savedRightPanelWidth.Value >= 260 ? _savedRightPanelWidth : new GridLength(450);

            if (colRightSplitter != null) colRightSplitter.Width = new GridLength(5);

        }

    }



    private void RightChatPanel_SizeChanged(object sender, SizeChangedEventArgs e)

    {

        double width = e.NewSize.Width;

        if (width <= 0) return;



        bool isNarrowHeader = width < 380;



        if (btnTools != null)

            btnTools.Content = isNarrowHeader ? "🛠️" : LocalizationManager.Instance["Araclar"];

        if (btnAgents != null)

            btnAgents.Content = isNarrowHeader ? "🤖" : LocalizationManager.Instance["AjanModulleri"];

        if (btnKeyboardShortcuts != null)

            btnKeyboardShortcuts.Content = isNarrowHeader ? "⌨️" : LocalizationManager.Instance["Kisayollar"];

        if (btnSettings != null)

            btnSettings.Content = isNarrowHeader ? "⚙️" : LocalizationManager.Instance["Ayarlar"];



        bool isNarrowComposer = width < 360;

        if (cmbContextMode != null && cmbContextMode.Items.Count >= 5)

        {

            cmbContextMode.Width = isNarrowComposer ? 46 : 88;

            cmbContextMode.Padding = isNarrowComposer ? new Thickness(2, 2, 2, 2) : new Thickness(4, 2, 4, 2);



            if (cmbContextMode.Items[0] is ComboBoxItem item0) item0.Content = isNarrowComposer ? "⚡" : "⚡ Auto";

            if (cmbContextMode.Items[1] is ComboBoxItem item1) item1.Content = isNarrowComposer ? "📄" : "📄 Dosya";

            if (cmbContextMode.Items[2] is ComboBoxItem item2) item2.Content = isNarrowComposer ? "✂️" : "✂️ Seçim";

            if (cmbContextMode.Items[3] is ComboBoxItem item3) item3.Content = isNarrowComposer ? "🔍" : "🔍 Proje";

            if (cmbContextMode.Items[4] is ComboBoxItem item4) item4.Content = isNarrowComposer ? "📚" : "📚 RAG";

        }



        if (cmbComposerModel != null && cmbComposerModel.Items.Count >= 4)

        {

            cmbComposerModel.Width = isNarrowComposer ? 46 : 105;

            cmbComposerModel.Padding = isNarrowComposer ? new Thickness(2, 2, 2, 2) : new Thickness(4, 2, 4, 2);



            if (cmbComposerModel.Items[0] is ComboBoxItem mItem0) mItem0.Content = isNarrowComposer ? "🤖" : "🤖 Otomatik";

            if (cmbComposerModel.Items[1] is ComboBoxItem mItem1) mItem1.Content = isNarrowComposer ? "🏠" : "🏠 Yerel Model";

            if (cmbComposerModel.Items[2] is ComboBoxItem mItem2) mItem2.Content = isNarrowComposer ? "⚡" : "⚡ Api Servisi";

            if (cmbComposerModel.Items[3] is ComboBoxItem mItem3) mItem3.Content = isNarrowComposer ? "☁️" : "☁️ Bulut Model";

        }



        if (btnSend != null)

        {

            btnSend.Width = isNarrowComposer ? 68 : 84;

            btnSend.Padding = isNarrowComposer ? new Thickness(4, 0, 4, 0) : new Thickness(8, 0, 8, 0);

        }

    }
    private void CmbWorkspaceMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (cmbWorkspaceMode?.SelectedItem is ComboBoxItem selectedItem)
        {
            var tag = selectedItem.Tag?.ToString();
            if (Enum.TryParse<AgentWorkspaceMode>(tag, out var newMode))
            {
                if (_settings.ActiveWorkspaceMode != newMode)
                {
                    _settings.ActiveWorkspaceMode = newMode;
                    SettingsWindow.SaveSettings(_settings);
                    
                    var modeName = newMode switch
                    {
                        AgentWorkspaceMode.CodeIDE => LocalizationManager.Instance.GetString("ModeCodeIDE"),
                        AgentWorkspaceMode.ImageStudio => LocalizationManager.Instance.GetString("ModeImageStudio"),
                        AgentWorkspaceMode.BlenderCopilot => LocalizationManager.Instance.GetString("ModeBlenderCopilot"),
                        AgentWorkspaceMode.UnityCopilot => LocalizationManager.Instance.GetString("ModeUnityCopilot"),
                        _ => newMode.ToString()
                    };
                    Notify(LocalizationManager.Instance.GetString("WorkspaceModeChanged", modeName), NotificationSeverity.Info);
                    AddTerminalMessage(LocalizationManager.Instance.GetString("WorkspaceModeChangedSystem", modeName));
                }
            }
        }
    }

    private void BtnModeSettings_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button btn && btn.Tag is string modeString)
        {
            if (Enum.TryParse<AgentWorkspaceMode>(modeString, out var mode))
            {
                var settingsWindow = new WorkspaceSettingsWindow(mode);
                settingsWindow.Owner = this;
                settingsWindow.ShowDialog();
                _settings = SettingsWindow.GetSettings();
            }
        }
    }
}
