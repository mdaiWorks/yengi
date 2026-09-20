using System.IO;
using System.Net.Http;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Linq;
using System.Collections.Generic;

namespace mdaiAgent;

// Hangi provider tipini kullandığımızı tutar
public enum ProviderType
{
    ApiService,   // OpenRouter, DeepSeek, Nvidia NIM vb.
    LocalModel,   // Ollama, LM Studio
    Anthropic,    // Claude
    Google,       // Gemini
    OpenAI        // Doğrudan OpenAI
}

// Çalışma Modları (Workspace Modes)
public enum AgentWorkspaceMode
{
    CodeIDE,
    ImageStudio,
    BlenderCopilot,
    UnityCopilot
}

public class AppSettings
{
    public string? GitHubUsername { get; set; }
    public string? GitHubToken { get; set; }
    public string? Language { get; set; }

    // Aktif Çalışma Modu (Workspace Mode)
    public AgentWorkspaceMode ActiveWorkspaceMode { get; set; } = AgentWorkspaceMode.CodeIDE;

    // Görsel Stüdyosu Ayarları
    public bool ImageStudioUseFreePollinations { get; set; } = true;
    public bool ImageStudioEnhancePrompt { get; set; } = false;
    public string ImageStudioPublicBaseUrl { get; set; } = "https://image.pollinations.ai/prompt/";
    public bool EnableMultiAgentImageGeneration { get; set; } = true;
    public string? ImageStudioApiKey { get; set; }
    public string ImageStudioBaseUrl { get; set; } = "https://api.openai.com/v1";
    public string ImageStudioModel { get; set; } = "dall-e-3";

    // Blender Ayarları
    public int BlenderWebSocketPort { get; set; } = 8181;
    public string BlenderSecretToken { get; set; } = Guid.NewGuid().ToString("N");
    public string? BlenderPath { get; set; }

    // Unity Ayarları
    public int UnityWebSocketPort { get; set; } = 8282;

    // Aktif provider tipi
    public ProviderType ActiveProvider { get; set; } = ProviderType.ApiService;

    // API Servisi ayarları (OpenRouter, DeepSeek vb.)
    public string? ApiKey { get; set; }
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";
    public string Model { get; set; } = "deepseek/deepseek-chat-v3-0324:free";

    // Yerel Model ayarları (Ollama vb.)
    public bool UseLocalModel { get; set; }
    public string LocalBaseUrl { get; set; } = "http://localhost:11434/v1";
    public string LocalModel { get; set; } = "qwen2.5-coder:7b";

    // Büyük model sağlayıcıları
    public string? AnthropicApiKey { get; set; }
    public string AnthropicBaseUrl { get; set; } = "https://api.anthropic.com/v1";
    public string AnthropicModel { get; set; } = "claude-sonnet-4-5";

    public string? GoogleApiKey { get; set; }
    public string GoogleBaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/openai";
    public string GoogleModel { get; set; } = "gemini-2.0-flash";

    public string? OpenAIApiKey { get; set; }
    public string OpenAIBaseUrl { get; set; } = "https://api.openai.com/v1";
    public string OpenAIModel { get; set; } = "gpt-4o";

    // Genel ayarlar
    public int ApiTimeoutSeconds { get; set; } = 600;
    public string SystemPrompt { get; set; } = "Sen Yengi adında, kullanıcının bilgisayarında çalışan, kod yazma, düzenleme ve dosya yönetimi araçlarına sahip gelişmiş bir yapay zeka yazılım asistanısın. Kullanıcıya net, kısa, teknik ve yapıcı cevaplar ver.";
    public bool AutoSaveEnabled { get; set; } = true;
    public int AutoSaveIntervalSeconds { get; set; } = 5;
    public bool TelemetryEnabled { get; set; } = true;
    public bool QaAgentEnabled { get; set; } = false;
    public bool UiAgentEnabled { get; set; } = false;
    public string? UploadEndpoint { get; set; }
    public string? TavilyApiKey { get; set; }

    // 🎤 Sesli Komut (STT) ayarları — Ana modelden bağımsız
    public string? SttApiKey { get; set; }
    public string SttBaseUrl { get; set; } = "https://api.groq.com/openai/v1";
    public string SttModel { get; set; } = "whisper-large-v3";

    // 🧠 RAG (Embedding) ayarları — Ana modelden bağımsız
    public string? RagApiKey { get; set; }
    public string RagBaseUrl { get; set; } = "https://api.openai.com/v1";
    public string RagModel { get; set; } = "text-embedding-3-small";
    public bool RagEnabled { get; set; } = false;

    // Araç Yönetimi (Tool Pruning)
    public List<string> DisabledTools { get; set; } = new();
    public List<string> RouterBlacklistedTools { get; set; } = new();
    // false = Otomatik Mod (model/router karar verir), true = Manuel Mod (kullanıcı seçer)
    public bool IsManualToolManagement { get; set; } = false;

    // AI Router Settings
    public bool RouterUseLocalModel { get; set; } = false;
    public bool RouterEnabled { get; set; } = false;
    public bool RouterUseMainModel { get; set; } = false;
    public double RouterConfidenceThreshold { get; set; } = 0.80;
    public string RouterBaseUrl { get; set; } = RouterModelInfo.DefaultBaseUrl;
    public string RouterModel { get; set; } = RouterModelInfo.OllamaModelName;
    public string? RouterApiKey { get; set; }

    public double GetNormalizedRouterConfidenceThreshold()
    {
        var value = RouterConfidenceThreshold;
        if (value > 1 && value <= 10)
            value /= 10;
        else if (value > 10 && value <= 100)
            value /= 100;

        return Math.Clamp(value, 0, 1);
    }

    /// <summary>
    /// Aktif provider'a göre kullanılacak BaseUrl'yi döndürür.
    /// NvidiaApiClient (OpenAI-compat client) bu değeri kullanır.
    /// </summary>
    public string GetActiveBaseUrl() => ActiveProvider switch
    {
        ProviderType.LocalModel  => LocalBaseUrl,
        ProviderType.Anthropic   => AnthropicBaseUrl,
        ProviderType.Google      => GoogleBaseUrl,
        ProviderType.OpenAI      => OpenAIBaseUrl,
        _                        => BaseUrl
    };

    /// <summary>
    /// Aktif provider'a göre kullanılacak API anahtarını döndürür.
    /// </summary>
    public string? GetActiveApiKey() => ActiveProvider switch
    {
        ProviderType.LocalModel  => null,
        ProviderType.Anthropic   => AnthropicApiKey,
        ProviderType.Google      => GoogleApiKey,
        ProviderType.OpenAI      => OpenAIApiKey,
        _                        => ApiKey
    };

    /// <summary>
    /// Aktif provider'a göre kullanılacak model adını döndürür.
    /// </summary>
    public string GetActiveModel() => ActiveProvider switch
    {
        ProviderType.LocalModel  => LocalModel,
        ProviderType.Anthropic   => AnthropicModel,
        ProviderType.Google      => GoogleModel,
        ProviderType.OpenAI      => OpenAIModel,
        _                        => Model
    };
}

public partial class SettingsWindow : Window
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Yengi",
        "settings.json"
    );

    public AppSettings Settings { get; private set; } = new();

    // Seçili sağlayıcı (büyük modeller için)
    private ProviderType _selectedBigProvider = ProviderType.Anthropic;

    // Kart kenarlık renkleri
    private static readonly SolidColorBrush SelectedCardBorder = new(Color.FromRgb(0x8b, 0x5c, 0xf6));
    private static readonly SolidColorBrush DefaultCardBorder  = new(Color.FromRgb(0x3f, 0x3f, 0x6f));
    private static readonly SolidColorBrush SelectedCardBg     = new(Color.FromRgb(0x20, 0x18, 0x38));
    private static readonly SolidColorBrush DefaultCardBg      = new(Color.FromRgb(0x1e, 0x1e, 0x35));

    public SettingsWindow()
    {
        InitializeComponent();
        LoadSettings();
        _ = CheckOllamaStatusAsync();
    }

    private void ApplyUiLanguage()
    {
        bool isEnglish = Localization.IsEnglish();
        bool isChinese = LocalizationManager.Instance.IsChinese;

        Title = isEnglish ? "Settings - Yengi" : "Ayarlar - Yengi";
        txtSettingsTitle.Text = Localization.Get("Ayarlar", "Settings");
        txtSettingsSubtitle.Text = Localization.Get(
            "Bağlantı, model ve davranış ayarlarını düzenleyin",
            "Configure connection, model and behavior settings");
        txtLanguageLabel.Text = Localization.Get(LocalizationManager.Instance.GetString("UygulamaDiliAppLanguage"), LocalizationManager.Instance.GetString("UygulamaDiliAppLanguage"));
        btnSettingsCancel.Content = Localization.Get("İptal", "Cancel");
        btnSettingsSave.Content = Localization.Get("✓  Kaydet", "✓  Save");
        rbLanguageEnglish.IsChecked = isEnglish;
        rbLanguageChinese.IsChecked = isChinese;
        rbLanguageTurkish.IsChecked = !isEnglish && !isChinese;
    }

    private void LoadSettings()
    {
        Settings = LoadSettingsFromDisk();
        if (string.IsNullOrWhiteSpace(Settings.Language))
            Settings.Language = "tr";

        if (Settings.RouterUseLocalModel &&
            (string.IsNullOrWhiteSpace(Settings.RouterModel)
             || Settings.RouterModel.Equals("mdai-router", StringComparison.OrdinalIgnoreCase)
             || Settings.RouterModel.Equals("gemini-2.5-flash", StringComparison.OrdinalIgnoreCase)
             || Settings.RouterModel.Equals("yengi-router:latest", StringComparison.OrdinalIgnoreCase)
             || Settings.RouterModel.Equals("yengi-router-1.5b-q4", StringComparison.OrdinalIgnoreCase)
             || Settings.RouterModel.Equals("yendi-router-1.5b-q4", StringComparison.OrdinalIgnoreCase)))
        {
            Settings.RouterModel = RouterModelInfo.OllamaModelName;
        }

        if (Settings.RouterUseLocalModel &&
            (string.IsNullOrWhiteSpace(Settings.RouterBaseUrl)
             || Settings.RouterBaseUrl.Contains("generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase)))
        {
            Settings.RouterBaseUrl = RouterModelInfo.DefaultBaseUrl;
        }

        rbLanguageEnglish.IsChecked = string.Equals(Settings.Language, "en", StringComparison.OrdinalIgnoreCase);
        rbLanguageChinese.IsChecked = string.Equals(Settings.Language, "zh", StringComparison.OrdinalIgnoreCase);
        rbLanguageTurkish.IsChecked = !rbLanguageEnglish.IsChecked.GetValueOrDefault() && !rbLanguageChinese.IsChecked.GetValueOrDefault();
        _initialLanguage = Settings.Language ?? "tr";
        ApplyUiLanguage();

        // API Servisi
        pbApiKey.Password           = Settings.ApiKey ?? "";
        txtApiKey.Text              = Settings.ApiKey ?? "";
        txtBaseUrl.Text             = Settings.BaseUrl;
        txtModel.Text               = Settings.Model;
        txtApiTimeoutSeconds.Text   = Settings.ApiTimeoutSeconds.ToString();
        pbTavilyApiKey.Password     = Settings.TavilyApiKey ?? "";
        txtTavilyApiKey.Text        = Settings.TavilyApiKey ?? "";

        // AI Router
        _isUpdatingRouterCheckboxes = true; // Yükleme sırasında CbRouterUseLocalModel_Changed tetiklenmesini engellemek için
        cbRouterUseLocalModel.IsChecked = Settings.RouterUseLocalModel;
        cbRouterEnabled.IsChecked       = Settings.RouterEnabled;
        cbRouterUseMainModel.IsChecked  = Settings.RouterUseMainModel;
        _isUpdatingRouterCheckboxes = false;
        
        txtRouterBaseUrl.Text           = Settings.RouterBaseUrl ?? RouterModelInfo.DefaultBaseUrl;
        txtRouterModel.Text             = Settings.RouterModel ?? RouterModelInfo.OllamaModelName;
        pbRouterApiKey.Password         = Settings.RouterApiKey ?? "";
        txtRouterApiKey.Text            = Settings.RouterApiKey ?? "";
        txtRouterConfidence.Text        = Settings.RouterConfidenceThreshold.ToString();
        UpdateRouterFieldsState();
        _ = UpdateLocalModelUIStatusAsync();

        // Yerel Model
        txtLocalBaseUrl.Text = Settings.LocalBaseUrl;
        txtLocalModel.Text   = Settings.LocalModel;

        // Büyük Modeller
        pbBigModelApiKey.Password = "";
        txtBigModelBaseUrl.Text   = "";
        txtBigModelModel.Text     = "";

        // Sistem & genel
        txtSystemPrompt.Text               = Settings.SystemPrompt;
        cbAutoSaveEnabled.IsChecked        = Settings.AutoSaveEnabled;
        txtAutoSaveIntervalSeconds.Text    = Settings.AutoSaveIntervalSeconds.ToString();
        cbTelemetryEnabled.IsChecked       = Settings.TelemetryEnabled;
        cbQaAgentEnabled.IsChecked         = Settings.QaAgentEnabled;
        cbUiAgentEnabled.IsChecked         = Settings.UiAgentEnabled;

        // Aktif kartı seç
        SelectActiveCard(Settings.ActiveProvider);
    }

    // ─────── Kart seçim mantığı ───────

    private void SelectActiveCard(ProviderType provider)
    {
        // Tüm kartları sıfırla
        ResetAllCards();

        if (provider == ProviderType.ApiService)
        {
            HighlightCard(cardApiService);
            panelApiService.Visibility = Visibility.Visible;
        }
        else if (provider == ProviderType.LocalModel)
        {
            HighlightCard(cardLocalModel);
            panelLocalModel.Visibility = Visibility.Visible;
        }
        else // Büyük modeller
        {
            HighlightCard(cardBigModels);
            panelBigModels.Visibility = Visibility.Visible;
            // Alt provider kartını da seç
            SelectBigModelProvider(provider);
        }
    }

    private void ResetAllCards()
    {
        cardApiService.BorderBrush  = DefaultCardBorder;
        cardApiService.Background   = DefaultCardBg;
        cardLocalModel.BorderBrush  = DefaultCardBorder;
        cardLocalModel.Background   = DefaultCardBg;
        cardBigModels.BorderBrush   = DefaultCardBorder;
        cardBigModels.Background    = DefaultCardBg;

        panelApiService.Visibility  = Visibility.Collapsed;
        panelLocalModel.Visibility  = Visibility.Collapsed;
        panelBigModels.Visibility   = Visibility.Collapsed;
    }

    private static void HighlightCard(System.Windows.Controls.Border card)
    {
        card.BorderBrush = SelectedCardBorder;
        card.Background  = SelectedCardBg;
    }

    private void SelectBigModelProvider(ProviderType provider)
    {
        _selectedBigProvider = provider;
        // Alt kartları sıfırla
        subCardAnthropic.BorderBrush = DefaultCardBorder;
        subCardGoogle.BorderBrush    = DefaultCardBorder;
        subCardOpenAI.BorderBrush    = DefaultCardBorder;

        switch (provider)
        {
            case ProviderType.Anthropic:
                subCardAnthropic.BorderBrush = SelectedCardBorder;
                txtBigModelBaseUrl.Text   = Settings.AnthropicBaseUrl;
                pbBigModelApiKey.Password = Settings.AnthropicApiKey ?? "";
                txtBigModelApiKey.Text   = Settings.AnthropicApiKey ?? "";
                txtBigModelModel.Text     = Settings.AnthropicModel;
                break;
            case ProviderType.Google:
                subCardGoogle.BorderBrush = SelectedCardBorder;
                txtBigModelBaseUrl.Text   = Settings.GoogleBaseUrl;
                pbBigModelApiKey.Password = Settings.GoogleApiKey ?? "";
                txtBigModelApiKey.Text   = Settings.GoogleApiKey ?? "";
                txtBigModelModel.Text     = Settings.GoogleModel;
                break;
            case ProviderType.OpenAI:
                subCardOpenAI.BorderBrush = SelectedCardBorder;
                txtBigModelBaseUrl.Text   = Settings.OpenAIBaseUrl;
                pbBigModelApiKey.Password = Settings.OpenAIApiKey ?? "";
                txtBigModelApiKey.Text   = Settings.OpenAIApiKey ?? "";
                txtBigModelModel.Text     = Settings.OpenAIModel;
                break;
        }

        bigModelFields.Visibility = Visibility.Visible;
    }

    // ─────── Kart tıklama olayları ───────

    private void CardApiService_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        Settings.ActiveProvider = ProviderType.ApiService;
        SelectActiveCard(ProviderType.ApiService);
    }

    private void CardLocalModel_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        Settings.ActiveProvider = ProviderType.LocalModel;
        SelectActiveCard(ProviderType.LocalModel);
    }

    private void CardBigModels_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Büyük modeller kartı seçince önce Anthropic'i varsayılan seç
        if (Settings.ActiveProvider != ProviderType.Anthropic &&
            Settings.ActiveProvider != ProviderType.Google &&
            Settings.ActiveProvider != ProviderType.OpenAI)
        {
            Settings.ActiveProvider = ProviderType.Anthropic;
        }
        SelectActiveCard(Settings.ActiveProvider);
    }

    private void SubCardAnthropic_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        Settings.ActiveProvider = ProviderType.Anthropic;
        SelectBigModelProvider(ProviderType.Anthropic);
    }

    private void SubCardGoogle_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        Settings.ActiveProvider = ProviderType.Google;
        SelectBigModelProvider(ProviderType.Google);
    }

    private void SubCardOpenAI_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        Settings.ActiveProvider = ProviderType.OpenAI;
        SelectBigModelProvider(ProviderType.OpenAI);
    }

    // ─────── Bağlantı Test butonları ───────

    #pragma warning disable VSTHRD100
    private async void BtnTestApiService_Click(object sender, RoutedEventArgs e)
    #pragma warning restore VSTHRD100
    {
        txtApiTestResult.Visibility = Visibility.Visible;
        txtApiTestResult.Text = "⏳ Test ediliyor...";
        txtApiTestResult.Foreground = new SolidColorBrush(Colors.Gray);

        var (ok, msg) = await TestConnectionAsync(txtBaseUrl.Text, pbApiKey.Password, txtModel.Text);
        txtApiTestResult.Text = ok ? $"✅ Bağlantı başarılı!" : $"❌ {msg}";
        txtApiTestResult.Foreground = ok
            ? new SolidColorBrush(Color.FromRgb(0x34, 0xd3, 0x99))
            : new SolidColorBrush(Color.FromRgb(0xf8, 0x71, 0x71));
    }

    #pragma warning disable VSTHRD100
    private async void BtnTestLocal_Click(object sender, RoutedEventArgs e)
    #pragma warning restore VSTHRD100
    {
        txtLocalTestResult.Visibility = Visibility.Visible;
        txtLocalTestResult.Text = "⏳ Test ediliyor...";
        txtLocalTestResult.Foreground = new SolidColorBrush(Colors.Gray);

        var (ok, msg) = await TestConnectionAsync(txtLocalBaseUrl.Text, null, txtLocalModel.Text);
        txtLocalTestResult.Text = ok ? LocalizationManager.Instance.GetString("BaglantiBasarili") : $"❌ {msg}";
        txtLocalTestResult.Foreground = ok
            ? new SolidColorBrush(Color.FromRgb(0x34, 0xd3, 0x99))
            : new SolidColorBrush(Color.FromRgb(0xf8, 0x71, 0x71));
    }

    #pragma warning disable VSTHRD100
    private async void BtnTestBigModel_Click(object sender, RoutedEventArgs e)
    #pragma warning restore VSTHRD100
    {
        txtBigModelTestResult.Visibility = Visibility.Visible;
        txtBigModelTestResult.Text = "⏳ Test ediliyor...";
        txtBigModelTestResult.Foreground = new SolidColorBrush(Colors.Gray);

        var (ok, msg) = await TestConnectionAsync(txtBigModelBaseUrl.Text, pbBigModelApiKey.Password, txtBigModelModel.Text);
        txtBigModelTestResult.Text = ok ? LocalizationManager.Instance.GetString("BaglantiBasarili") : $"❌ {msg}";
        txtBigModelTestResult.Foreground = ok
            ? new SolidColorBrush(Color.FromRgb(0x34, 0xd3, 0x99))
            : new SolidColorBrush(Color.FromRgb(0xf8, 0x71, 0x71));
    }

    /// <summary>
    /// Basit bağlantı testi: modeller listesini çekmeye çalışır.
    /// </summary>
    private static async Task<(bool Success, string Message)> TestConnectionAsync(string baseUrl, string? apiKey, string model)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            if (!string.IsNullOrEmpty(apiKey))
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + apiKey.Trim());

            var url = baseUrl.TrimEnd('/') + "/models";
            var resp = await client.GetAsync(url);
            return resp.IsSuccessStatusCode
                ? (true, "OK")
                : (false, $"HTTP {(int)resp.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    // ─────── Ollama canlı durum kontrolü ───────

    private async Task CheckOllamaStatusAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var resp = await client.GetAsync("http://localhost:11434/api/version");
            Dispatcher.Invoke(() =>
            {
                ollamaStatusDot.Fill  = new SolidColorBrush(Color.FromRgb(0x34, 0xd3, 0x99));
                ollamaStatusText.Text = LocalizationManager.Instance.GetString("OllamaCalisiyor");
                ollamaStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0xd3, 0x99));
            });
        }
        catch
        {
            Dispatcher.Invoke(() =>
            {
                ollamaStatusDot.Fill  = new SolidColorBrush(Color.FromRgb(0xf8, 0x71, 0x71));
                ollamaStatusText.Text = LocalizationManager.Instance.GetString("OllamaBulunamadi");
                ollamaStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x6b, 0x6b, 0x9a));
            });
        }
    }

    // ─────── Kaydet / İptal ───────

    private void SaveSettings()
    {
        Settings.Language = rbLanguageEnglish.IsChecked == true
            ? "en"
            : rbLanguageChinese.IsChecked == true ? "zh" : "tr";
        App.ApplyLanguage(Settings.Language);
        Localization.SetLanguage(Settings.Language);
        ApplyUiLanguage();

        // API Servisi
        Settings.ApiKey             = pbApiKey.Visibility == Visibility.Visible ? pbApiKey.Password : txtApiKey.Text;
        Settings.BaseUrl            = txtBaseUrl.Text.Trim().TrimEnd('/');
        Settings.Model              = txtModel.Text.Trim();
        Settings.ApiTimeoutSeconds  = int.TryParse(txtApiTimeoutSeconds.Text, out var t) && t > 0 ? t : 600;
        Settings.TavilyApiKey       = pbTavilyApiKey.Visibility == Visibility.Visible ? pbTavilyApiKey.Password.Trim() : txtTavilyApiKey.Text.Trim();

        // AI Router
        Settings.RouterUseLocalModel  = cbRouterUseLocalModel.IsChecked == true;
        Settings.RouterEnabled        = cbRouterEnabled.IsChecked == true;
        Settings.RouterUseMainModel   = cbRouterUseMainModel.IsChecked == true;
        Settings.RouterBaseUrl        = txtRouterBaseUrl.Text.Trim().TrimEnd('/');
        Settings.RouterModel          = txtRouterModel.Text.Trim();
        Settings.RouterApiKey         = pbRouterApiKey.Visibility == Visibility.Visible ? pbRouterApiKey.Password.Trim() : txtRouterApiKey.Text.Trim();
        if (TryParseRouterConfidence(txtRouterConfidence.Text, out var conf))
            Settings.RouterConfidenceThreshold = conf;

        // Yerel Model
        Settings.LocalBaseUrl = txtLocalBaseUrl.Text.Trim().TrimEnd('/');
        Settings.LocalModel   = txtLocalModel.Text.Trim();

        // Büyük Modeller - aktif sağlayıcıya göre kaydet
        string currentBigKey = pbBigModelApiKey.Visibility == Visibility.Visible ? pbBigModelApiKey.Password : txtBigModelApiKey.Text;
        switch (_selectedBigProvider)
        {
            case ProviderType.Anthropic:
                Settings.AnthropicApiKey  = currentBigKey;
                Settings.AnthropicBaseUrl = txtBigModelBaseUrl.Text.Trim().TrimEnd('/');
                Settings.AnthropicModel   = txtBigModelModel.Text.Trim();
                break;
            case ProviderType.Google:
                Settings.GoogleApiKey  = currentBigKey;
                Settings.GoogleBaseUrl = txtBigModelBaseUrl.Text.Trim().TrimEnd('/');
                Settings.GoogleModel   = txtBigModelModel.Text.Trim();
                break;
            case ProviderType.OpenAI:
                Settings.OpenAIApiKey  = currentBigKey;
                Settings.OpenAIBaseUrl = txtBigModelBaseUrl.Text.Trim().TrimEnd('/');
                Settings.OpenAIModel   = txtBigModelModel.Text.Trim();
                break;
        }

        // Genel
        Settings.SystemPrompt            = txtSystemPrompt.Text.Trim();
        Settings.AutoSaveEnabled         = cbAutoSaveEnabled.IsChecked == true;
        Settings.AutoSaveIntervalSeconds = int.TryParse(txtAutoSaveIntervalSeconds.Text, out var iv) && iv > 0 ? iv : 5;
        Settings.TelemetryEnabled        = cbTelemetryEnabled.IsChecked == true;
        Settings.QaAgentEnabled          = cbQaAgentEnabled.IsChecked == true;
        Settings.UiAgentEnabled          = cbUiAgentEnabled.IsChecked == true;

        SaveSettings(Settings);
    }

    private static bool TryParseRouterConfidence(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalizedText = text.Trim().Replace(',', '.');
        if (!double.TryParse(normalizedText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return false;

        value = parsed > 1 && parsed <= 10
            ? parsed / 10
            : parsed > 10 && parsed <= 100
                ? parsed / 100
                : parsed;
        value = Math.Clamp(value, 0, 1);
        return true;
    }

    public static event EventHandler? SettingsSaved;

    public static void SaveSettings(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Language))
            settings.Language = "tr";

        var dir = Path.GetDirectoryName(SettingsPath)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        SecretStore.Save(settings);
        var settingsJson = JsonSerializer.SerializeToNode(settings)!.AsObject();
        SecretStore.RemoveSecretsFromJson(settingsJson);
        File.WriteAllText(SettingsPath, settingsJson.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        SettingsSaved?.Invoke(null, EventArgs.Empty);
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        string previousLanguage = _initialLanguage;
        SaveSettings();

        if (!string.Equals(previousLanguage, Settings.Language, StringComparison.OrdinalIgnoreCase))
        {
            var result = MessageBox.Show(
                LocalizationManager.Instance.GetString("DilDegisikligiYenidenBaslatGerekli"),
                LocalizationManager.Instance.GetString("YenidenBaslatBaslik"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                if (!string.IsNullOrEmpty(Environment.ProcessPath))
                {
                    System.Diagnostics.Process.Start(Environment.ProcessPath);
                }
                System.Windows.Application.Current.Shutdown();
                return;
            }
        }

        DialogResult = true;
        Close();
    }

    private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private string _initialLanguage = "tr";
    private bool _isUpdatingRouterCheckboxes = false;
    private bool _localRouterRegistered;

    private void UpdateRouterFieldsState()
    {
        bool useLocalModel = cbRouterUseLocalModel.IsChecked == true;
        bool routerOn      = cbRouterEnabled.IsChecked == true;
        bool useMainModel  = cbRouterUseMainModel.IsChecked == true;

        bool anyRouterActive = useLocalModel || routerOn || useMainModel;
        bool customFieldsActive = routerOn;

        cbRouterUseLocalModel.IsEnabled = _localRouterRegistered;
        cbRouterEnabled.IsEnabled = true;
        cbRouterUseMainModel.IsEnabled = true;

        txtRouterBaseUrl.IsEnabled     = customFieldsActive;
        txtRouterModel.IsEnabled       = customFieldsActive;
        pbRouterApiKey.IsEnabled       = customFieldsActive;
        txtRouterApiKey.IsEnabled      = customFieldsActive;
        btnToggleRouterKey.IsEnabled   = customFieldsActive;
        txtRouterConfidence.IsEnabled  = anyRouterActive;
    }

    private void BtnToggleKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            if (btn == btnToggleMainKey)
            {
                TogglePasswordVisibility(pbApiKey, txtApiKey, btn);
            }
            else if (btn == btnToggleBigModelKey)
            {
                TogglePasswordVisibility(pbBigModelApiKey, txtBigModelApiKey, btn);
            }
            else if (btn == btnToggleTavilyKey)
            {
                TogglePasswordVisibility(pbTavilyApiKey, txtTavilyApiKey, btn);
            }
            else if (btn == btnToggleRouterKey)
            {
                TogglePasswordVisibility(pbRouterApiKey, txtRouterApiKey, btn);
            }
        }
    }

    private static void TogglePasswordVisibility(System.Windows.Controls.PasswordBox pb, System.Windows.Controls.TextBox txt, System.Windows.Controls.Button btn)
    {
        if (pb.Visibility == Visibility.Visible)
        {
            txt.Text = pb.Password;
            pb.Visibility = Visibility.Collapsed;
            txt.Visibility = Visibility.Visible;
            btn.Content = "🙈";
        }
        else
        {
            pb.Password = txt.Text;
            txt.Visibility = Visibility.Collapsed;
            pb.Visibility = Visibility.Visible;
            btn.Content = "👁️";
        }
    }

    private void CbRouterUseLocalModel_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingRouterCheckboxes) return;

        if (cbRouterUseLocalModel.IsChecked == true)
        {
            if (!_localRouterRegistered)
            {
                _isUpdatingRouterCheckboxes = true;
                cbRouterUseLocalModel.IsChecked = false;
                _isUpdatingRouterCheckboxes = false;
                MessageBox.Show(
                    LocalizationManager.Instance.GetString("YerelRouterModeliGerekli"),
                    LocalizationManager.Instance.GetString("ModelGerekli"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                UpdateRouterFieldsState();
                return;
            }

            var settings = GetSettings();
            if (settings.IsManualToolManagement)
            {
                MessageBox.Show(LocalizationManager.Instance.GetString("AraclarMenusundeManuelModAktifRouterIAcabilmekIcinLutfenAraclarMenusundenOtomatikModAGecin"), LocalizationManager.Instance.GetString("Uyari2"), MessageBoxButton.OK, MessageBoxImage.Warning);
                _isUpdatingRouterCheckboxes = true;
                cbRouterUseLocalModel.IsChecked = false;
                _isUpdatingRouterCheckboxes = false;
                return;
            }

            _isUpdatingRouterCheckboxes = true;
            cbRouterEnabled.IsChecked = false;
            cbRouterUseMainModel.IsChecked = false;
            _isUpdatingRouterCheckboxes = false;
        }
        UpdateRouterFieldsState();
    }

    private void CbRouterEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingRouterCheckboxes) return;

        if (cbRouterEnabled.IsChecked == true)
        {
            var settings = GetSettings();
            if (settings.IsManualToolManagement)
            {
                MessageBox.Show(LocalizationManager.Instance.GetString("AraclarMenusundeManuelModAktifRouterIAcabilmekIcinLutfenAraclarMenusundenOtomatikModAGecin"), LocalizationManager.Instance.GetString("Uyari2"), MessageBoxButton.OK, MessageBoxImage.Warning);
                _isUpdatingRouterCheckboxes = true;
                cbRouterEnabled.IsChecked = false;
                _isUpdatingRouterCheckboxes = false;
                return;
            }

            _isUpdatingRouterCheckboxes = true;
            cbRouterUseLocalModel.IsChecked = false;
            cbRouterUseMainModel.IsChecked = false;
            _isUpdatingRouterCheckboxes = false;
        }
        UpdateRouterFieldsState();
    }

    private void CbRouterUseMainModel_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingRouterCheckboxes) return;

        if (cbRouterUseMainModel.IsChecked == true)
        {
            var settings = GetSettings();
            if (settings.IsManualToolManagement)
            {
                MessageBox.Show(LocalizationManager.Instance.GetString("AraclarMenusundeManuelModAktifRouterIAcabilmekIcinLutfenAraclarMenusundenOtomatikModAGecin"), LocalizationManager.Instance.GetString("Uyari2"), MessageBoxButton.OK, MessageBoxImage.Warning);
                _isUpdatingRouterCheckboxes = true;
                cbRouterUseMainModel.IsChecked = false;
                _isUpdatingRouterCheckboxes = false;
                return;
            }

            _isUpdatingRouterCheckboxes = true;
            cbRouterUseLocalModel.IsChecked = false;
            cbRouterEnabled.IsChecked = false;
            _isUpdatingRouterCheckboxes = false;
        }
        UpdateRouterFieldsState();
    }

    public static string LocalModelPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Yengi", "models", RouterModelInfo.ModelFileName);

    public static string LocalModelDownloadUrl { get; set; } = $"https://huggingface.co/mdaiworks/yengi-router-1.5b/resolve/main/{RouterModelInfo.ModelFileName}";

    private async Task UpdateLocalModelUIStatusAsync()
    {
        bool fileExists = System.IO.File.Exists(LocalModelPath);
        bool registered = fileExists && await IsLocalRouterRegisteredWithOllamaAsync();
        bool ollamaInstalled = await CheckIfOllamaInstalledAsync();

        _localRouterRegistered = registered;
        if (!registered && cbRouterUseLocalModel.IsChecked == true)
        {
            _isUpdatingRouterCheckboxes = true;
            cbRouterUseLocalModel.IsChecked = false;
            _isUpdatingRouterCheckboxes = false;
        }
        UpdateRouterFieldsState();

        if (registered)
        {
            txtLocalModelStatus.Text = "🟢 yengi-router:1.5b modeli başarıyla aktif edildi ve çalışıyor!";
            txtLocalModelStatus.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#10b981"));
            btnInstallOllama.Visibility = Visibility.Collapsed;
            btnCheckOllama.Visibility = Visibility.Collapsed;
            btnSelectLocalGguf.Visibility = Visibility.Visible;
            btnSelectLocalGguf.Content = LocalizationManager.Instance.GetString("GgufDosyasiSec");
            btnDownloadLocalModel.Visibility = Visibility.Visible;
            btnDownloadLocalModel.Content = LocalizationManager.Instance.GetString("YerelRouterModeliGuncelle");
            pbLocalModelDownload.Visibility = Visibility.Collapsed;
        }
        else if (!ollamaInstalled)
        {
            txtLocalModelStatus.Text = LocalizationManager.Instance.GetString("OllamaKuruluDegilRouterIcinGerekli");
            txtLocalModelStatus.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#f87171"));
            btnInstallOllama.Visibility = Visibility.Visible;
            btnCheckOllama.Visibility = Visibility.Visible;
            btnSelectLocalGguf.Visibility = fileExists ? Visibility.Visible : Visibility.Collapsed;
            btnDownloadLocalModel.Visibility = Visibility.Collapsed;
        }
        else if (!fileExists)
        {
            txtLocalModelStatus.Text = LocalizationManager.Instance.GetString("YerelRouterModeliHenuzYuklenmedi");
            txtLocalModelStatus.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#fbbf24"));
            btnInstallOllama.Visibility = Visibility.Collapsed;
            btnCheckOllama.Visibility = Visibility.Collapsed;
            btnSelectLocalGguf.Visibility = Visibility.Visible;
            btnDownloadLocalModel.Visibility = Visibility.Visible;
            btnDownloadLocalModel.Content = LocalizationManager.Instance.GetString("YengiRouterIndirUcretsiz");
        }
        else
        {
            txtLocalModelStatus.Text = LocalizationManager.Instance.GetString("GgufDosyasiHazirOllamayaKaydet");
            txtLocalModelStatus.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#60a5fa"));
            btnInstallOllama.Visibility = Visibility.Collapsed;
            btnCheckOllama.Visibility = Visibility.Collapsed;
            btnSelectLocalGguf.Visibility = Visibility.Visible;
            btnDownloadLocalModel.Visibility = Visibility.Visible;
            btnDownloadLocalModel.Content = LocalizationManager.Instance.GetString("ModeliKaydet");
        }
    }

    public static string? GetOllamaExePath()
    {
        try
        {
            // 1. Check default installation paths on Windows (AppData Local Programs & Program Files)
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string userPath = System.IO.Path.Combine(localAppData, "Programs", "Ollama", "ollama.exe");
            if (System.IO.File.Exists(userPath)) return userPath;

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pfPath = System.IO.Path.Combine(programFiles, "Ollama", "ollama.exe");
            if (System.IO.File.Exists(pfPath)) return pfPath;

            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string pfX86Path = System.IO.Path.Combine(programFilesX86, "Ollama", "ollama.exe");
            if (System.IO.File.Exists(pfX86Path)) return pfX86Path;

            // 2. Scan PATH environment variables (Process, User, Machine)
            string processPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process) ?? "";
            string userPathEnv = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
            string machinePathEnv = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? "";

            var allPaths = (processPath + ";" + userPathEnv + ";" + machinePathEnv)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var dir in allPaths)
            {
                try
                {
                    string candidate = System.IO.Path.Combine(dir, "ollama.exe");
                    if (System.IO.File.Exists(candidate))
                        return candidate;
                }
                catch { }
            }
        }
        catch { }

        return null;
    }

    private static async Task<bool> CheckIfOllamaInstalledAsync()
    {
        // Method 1: HTTP API Ping to local Ollama service (127.0.0.1:11434)
        try
        {
            using var handler = new System.Net.Http.HttpClientHandler();
            using var client = new System.Net.Http.HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
            var response = await client.GetAsync("http://127.0.0.1:11434/api/tags");
            if (response.IsSuccessStatusCode)
                return true;
        }
        catch { }

        // Method 2: Check if ollama.exe executable exists on disk
        string? exePath = GetOllamaExePath();
        if (!string.IsNullOrEmpty(exePath) && System.IO.File.Exists(exePath))
        {
            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var process = new System.Diagnostics.Process { StartInfo = startInfo };
                if (process.Start())
                {
                    await process.WaitForExitAsync();
                    return process.ExitCode == 0;
                }
            }
            catch { }

            // File exists on disk even if process execution failed
            return true;
        }

        // Method 3: Fallback process launch with "ollama"
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ollama",
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var process = new System.Diagnostics.Process { StartInfo = startInfo };
            if (!process.Start()) return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private void BtnInstallOllama_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://ollama.com") { UseShellExecute = true });
            MessageBox.Show(LocalizationManager.Instance.GetString("OllamaIndiripKurduktanSonraKontrolEt"), LocalizationManager.Instance.GetString("Information"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{LocalizationManager.Instance.GetString("BaglantiAcilamadi")}: {ex.Message}", LocalizationManager.Instance.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

#pragma warning disable VSTHRD100
    private async void BtnCheckOllama_Click(object sender, RoutedEventArgs e)
#pragma warning restore VSTHRD100
    {
        try
        {
            btnCheckOllama.IsEnabled = false;
            btnCheckOllama.Content = LocalizationManager.Instance.GetString("KontrolEdiliyor");

            // Give a short visual delay so user sees button loading state
            await Task.Delay(400);

            await UpdateLocalModelUIStatusAsync();

            bool isInstalled = await CheckIfOllamaInstalledAsync();
            if (isInstalled)
            {
                MessageBox.Show(LocalizationManager.Instance.GetString("OllamaTespitEdildi"), LocalizationManager.Instance.GetString("OllamaDurumu"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(LocalizationManager.Instance.GetString("OllamaHenuzTespitEdilemedi"), LocalizationManager.Instance.GetString("OllamaDurumu"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BtnCheckOllama Error] {ex.Message}");
        }
        finally
        {
            if (btnCheckOllama != null)
            {
                btnCheckOllama.IsEnabled = true;
                btnCheckOllama.Content = LocalizationManager.Instance.GetString("KontrolEt");
            }
        }
    }

#pragma warning disable VSTHRD100
    private async void BtnSelectLocalGguf_Click(object sender, RoutedEventArgs e)
#pragma warning restore VSTHRD100
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "GGUF Model (*.gguf)|*.gguf|All Files (*.*)|*.*",
            Title = LocalizationManager.Instance.GetString("GgufSecinTitle")
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                btnSelectLocalGguf.IsEnabled = false;
                txtLocalModelStatus.Text = LocalizationManager.Instance.GetString("GgufDosyasiTasiniyor");

                var targetDir = System.IO.Path.GetDirectoryName(LocalModelPath);
                if (!string.IsNullOrEmpty(targetDir) && !System.IO.Directory.Exists(targetDir))
                {
                    System.IO.Directory.CreateDirectory(targetDir);
                }

                await Task.Run(() => System.IO.File.Copy(dialog.FileName, LocalModelPath, true));

                txtLocalModelStatus.Text = LocalizationManager.Instance.GetString("ModeliOllamayaKaydediliyor");
                await RegisterLocalRouterWithOllamaAsync(LocalModelPath);

                Settings.RouterModel = RouterModelInfo.OllamaModelName;
                txtRouterModel.Text = RouterModelInfo.OllamaModelName;
                SaveSettings(Settings);

                MessageBox.Show(LocalizationManager.Instance.GetString("YengiRouterModeliKurulduVeKaydedildi"), LocalizationManager.Instance.GetString("SuccessTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                await UpdateLocalModelUIStatusAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{LocalizationManager.Instance.GetString("ModelYuklenirkenHata")}: {ex.Message}", LocalizationManager.Instance.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                await UpdateLocalModelUIStatusAsync();
            }
            finally
            {
                btnSelectLocalGguf.IsEnabled = true;
            }
        }
    }

    private static async Task<bool> IsLocalRouterRegisteredWithOllamaAsync()
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "ollama",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("show");
        startInfo.ArgumentList.Add(RouterModelInfo.OllamaModelName);

        try
        {
            using var process = new System.Diagnostics.Process { StartInfo = startInfo };
            if (!process.Start()) return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private void BtnDownloadLocalModel_Click(object sender, RoutedEventArgs e)
    {
        _ = DownloadLocalModelAsync();
    }

    private async Task DownloadLocalModelAsync()
    {
        if (!File.Exists(LocalModelPath) && (string.IsNullOrEmpty(LocalModelDownloadUrl) || LocalModelDownloadUrl.Contains("YOUR_USERNAME")))
        {
            MessageBox.Show("İndirme bağlantısı henüz girilmedi. Lütfen HuggingFace veya indirme URL'sini ekleyin.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        bool fileExists = File.Exists(LocalModelPath);
        bool registered = fileExists && await IsLocalRouterRegisteredWithOllamaAsync();

        if (registered)
        {
            var confirm = MessageBox.Show(
                LocalizationManager.Instance.GetString("YerelRouterModeliGuncellensinMi"),
                LocalizationManager.Instance.GetString("ModeliGuncelleTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                if (File.Exists(LocalModelPath))
                {
                    File.Delete(LocalModelPath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Eski model dosyası silinemedi: {ex.Message}", LocalizationManager.Instance.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }
        else if (fileExists)
        {
            btnDownloadLocalModel.IsEnabled = false;
            btnDownloadLocalModel.Content = LocalizationManager.Instance.GetString("YerelRouterKuruluyor");
            txtLocalModelStatus.Text = LocalizationManager.Instance.GetString("YerelRouterGgufOllamayaKaydediliyor");
            try
            {
                await RegisterLocalRouterWithOllamaAsync(LocalModelPath);
                Settings.RouterModel = RouterModelInfo.OllamaModelName;
                txtRouterModel.Text = RouterModelInfo.OllamaModelName;
                SaveSettings(Settings);
                MessageBox.Show(LocalizationManager.Instance.GetString("YerelRouterKurulumBasarili"), LocalizationManager.Instance.GetString("Basarili"), MessageBoxButton.OK, MessageBoxImage.Information);
                await UpdateLocalModelUIStatusAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Router modeli Ollama'ya kaydedilemedi: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                txtLocalModelStatus.Text = LocalizationManager.Instance.GetString("YerelRouterKurulumBasarisiz");
            }
            finally
            {
                btnDownloadLocalModel.IsEnabled = true;
            }

            return;
        }

        btnDownloadLocalModel.IsEnabled = false;
        btnDownloadLocalModel.Content = "İndiriliyor...";
        pbLocalModelDownload.Visibility = Visibility.Visible;
        pbLocalModelDownload.Value = 0;
        txtLocalModelStatus.Text = LocalizationManager.Instance.GetString("YerelRouterModelIndiriliyor");

        try
        {
            var dir = System.IO.Path.GetDirectoryName(LocalModelPath);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
            }

            using var httpClient = new System.Net.Http.HttpClient();
            using var response = await httpClient.GetAsync(LocalModelDownloadUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            using var stream = await response.Content.ReadAsStreamAsync();
            using var fileStream = System.IO.File.Create(LocalModelPath);

            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                totalRead += bytesRead;

                if (totalBytes > 0)
                {
                    double progress = (double)totalRead / totalBytes * 100.0;
                    Dispatcher.Invoke(() =>
                    {
                        pbLocalModelDownload.Value = progress;
                        txtLocalModelStatus.Text = $"İndiriliyor... %{progress:F1} ({totalRead / (1024 * 1024)} / {totalBytes / (1024 * 1024)} MB)";
                    });
                }
            }

            // Dosya kilitlenmesini (file lock) önlemek için indirme akışlarını Ollama'ya geçmeden önce kapatıyoruz
            await fileStream.FlushAsync();
            fileStream.Close();
            stream.Close();

            txtLocalModelStatus.Text = LocalizationManager.Instance.GetString("YerelRouterGgufOllamayaKaydediliyor");
            await RegisterLocalRouterWithOllamaAsync(LocalModelPath);

            Settings.RouterModel = RouterModelInfo.OllamaModelName;
            txtRouterModel.Text = RouterModelInfo.OllamaModelName;
            SaveSettings(Settings);

            string successMsg = registered 
                ? LocalizationManager.Instance.GetString("YerelRouterModelGuncellendi") 
                : LocalizationManager.Instance.GetString("YerelRouterIndirmeKurulumBasarili");
            MessageBox.Show(successMsg, LocalizationManager.Instance.GetString("Basarili"), MessageBoxButton.OK, MessageBoxImage.Information);
            await UpdateLocalModelUIStatusAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Model indirilirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            txtLocalModelStatus.Text = LocalizationManager.Instance.GetString("YerelRouterIndirmeBasarisiz");
            btnDownloadLocalModel.IsEnabled = true;
            btnDownloadLocalModel.Content = "Yeniden Dene";
        }
    }

    private static async Task RegisterLocalRouterWithOllamaAsync(string modelPath)
    {
        var modelDirectory = Path.GetDirectoryName(modelPath);
        if (string.IsNullOrWhiteSpace(modelDirectory))
            throw new InvalidOperationException("Router model klasörü bulunamadı.");

        var modelfilePath = Path.Combine(modelDirectory, "Modelfile.mdai-router");
        await File.WriteAllTextAsync(modelfilePath, $"FROM \"{modelPath.Replace("\\", "/")}\"{Environment.NewLine}");

        string exePath = GetOllamaExePath() ?? "ollama";
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("create");
        startInfo.ArgumentList.Add(RouterModelInfo.OllamaModelName);
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add(modelfilePath);

        using var process = new System.Diagnostics.Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("Ollama işlemi başlatılamadı.");

        var standardErrorTask = process.StandardError.ReadToEndAsync();
        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var error = await standardErrorTask;
            var output = await standardOutputTask;
            throw new InvalidOperationException($"Ollama model kaydı başarısız oldu: {error.Trim()} {output.Trim()}".Trim());
        }
    }

    private void BtnRouterBlacklist_Click(object sender, RoutedEventArgs e)
    {
        var settings = GetSettings();
        var currentBlacklist = settings.RouterBlacklistedTools ?? new List<string>();

        var window = new RouterBlacklistWindow(currentBlacklist)
        {
            Owner = this
        };

        if (window.ShowDialog() == true)
        {
            settings.RouterBlacklistedTools = window.ToolItems
                .Where(t => t.IsBlacklisted)
                .Select(t => t.Name)
                .ToList();
            SaveSettings(settings);
            LoadSettings(); // UI'ı tazelemek için
        }
    }

    public static AppSettings GetSettings()
    {
        return LoadSettingsFromDisk();
    }

    private static AppSettings LoadSettingsFromDisk()
    {
        AppSettings settings = new();
        if (File.Exists(SettingsPath))
        {
            var json = File.ReadAllText(SettingsPath);
            settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }

        var legacySecrets = new Dictionary<string, string?>
        {
            ["ApiKey"] = settings.ApiKey,
            ["AnthropicApiKey"] = settings.AnthropicApiKey,
            ["GoogleApiKey"] = settings.GoogleApiKey,
            ["OpenAIApiKey"] = settings.OpenAIApiKey,
            ["TavilyApiKey"] = settings.TavilyApiKey,
            ["RouterApiKey"] = settings.RouterApiKey,
            ["GitHubToken"] = settings.GitHubToken
        };

        var storedSecrets = SecretStore.Load();
        SecretStore.Apply(settings, storedSecrets);

        if (legacySecrets.Values.Any(value => !string.IsNullOrWhiteSpace(value)) && storedSecrets.Count == 0)
            SaveSettings(settings);

        return settings;
    }
}
