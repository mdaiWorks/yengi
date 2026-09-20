using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace mdaiAgent;

public partial class RagSettingsWindow : Window
{
    public AppSettings Settings { get; }
    public bool DisabledRequested { get; private set; } = false;

    public RagSettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        Settings = settings;
        ApplyLanguage();

        // Kayıtlı değerleri doldur
        txtRagBaseUrl.Text   = settings.RagBaseUrl;
        pbRagApiKey.Password = settings.RagApiKey ?? "";
        txtRagModel.Text     = settings.RagModel;

        // Eğer Ollama URL'si ile kuruluysa ipucunu göster
        if (settings.RagBaseUrl.Contains("localhost") || settings.RagBaseUrl.Contains("127.0.0.1"))
            ollamaHint.Visibility = Visibility.Visible;
    }

    private void ApplyLanguage()
    {
        bool isEnglish = Localization.IsEnglish();

        Title = isEnglish ? "RAG — Project Intelligence Settings" : LocalizationManager.Instance.GetString("RAGButunselProjeZekasiAyarlari");
        txtTitle.Text = isEnglish ? "RAG — Project Intelligence" : "RAG — Bütünsel Proje Zekası";
        txtSubtitle.Text = isEnglish
            ? "Enter an embedding API to explain your project to AI"
            : "Projenizi AI'a tam anlatmak için bir Embedding API girin";
        txtQuickTemplateTitle.Text = isEnglish ? LocalizationManager.Instance.GetString("HIZLISABLONSEC") : "HIZLI ŞABLON SEÇ";
        txtCloudCardTitle.Text = isEnglish ? "Cloud API" : "Bulut API";
        txtCloudCardDesc.Text = isEnglish
            ? "Any OpenAI-compatible provider (requires API key)"
            : "OpenAI uyumlu herhangi bir sağlayıcı (API key gerektirir)";
        txtCloudCardArrow.Text = isEnglish ? "Template →" : "Şablon →";
        txtLocalCardTitle.Text = isEnglish ? "Local Model" : "Yerel Model";
        txtLocalCardDesc.Text = isEnglish
            ? "OpenAI-compatible embedding server running on your machine"
            : "Bilgisayarınızda çalışan OpenAI uyumlu embedding sunucusu";
        txtLocalCardArrow.Text = isEnglish ? "Template →" : "Şablon →";
        txtApiInfoTitle.Text = isEnglish ? LocalizationManager.Instance.GetString("APIBILGILERI") : "API BİLGİLERİ";
        txtBaseUrlLabel.Text = "Base URL";
        txtApiKeyLabel.Text = isEnglish ? "API Key" : "API Anahtarı";
        txtModelLabel.Text = isEnglish ? "Model Name" : "Model Adı";
        txtLocalSetupTitle.Text = isEnglish ? "🏠 About Local Model Setup" : "🏠 Yerel Model Kurulum Hakkında";
        txtLocalSetupBody.Inlines.Clear();
        if (isEnglish)
        {
            txtLocalSetupBody.Inlines.Add(new Run("To use a local model, an OpenAI-compatible embedding server must be running on your machine."));
            txtLocalSetupBody.Inlines.Add(new LineBreak());
            txtLocalSetupBody.Inlines.Add(new Run("Enter the address being listened to in Base URL and the supported embedding model name in Model Name."));
            txtLocalSetupBody.Inlines.Add(new LineBreak());
            txtLocalSetupBody.Inlines.Add(new Run("Servers that do not require an API key can leave this field blank."));
        }
        else
        {
            txtLocalSetupBody.Inlines.Clear();
            txtLocalSetupBody.Inlines.Add(new Run("Yerel model kullanmak için bilgisayarınızda OpenAI uyumlu bir embedding sunucusu çalışıyor olmalıdır."));
            txtLocalSetupBody.Inlines.Add(new LineBreak());
            txtLocalSetupBody.Inlines.Add(new Run("Sunucunun dinlediği adresi Base URL'e, desteklediği embedding model adını Model Adı'na yazın."));
            txtLocalSetupBody.Inlines.Add(new LineBreak());
            txtLocalSetupBody.Inlines.Add(new Run("API Key gerektirmeyen sunucularda bu alanı boş bırakabilirsiniz."));
        }

        btnDisable.Content = isEnglish ? "🔴 Disable RAG" : "🔴 RAG'ı Devre Dışı Bırak";
        btnCancel.Content = isEnglish ? "Cancel" : "İptal";
        btnSave.Content = isEnglish ? "✓ Save & Activate" : "✓ Kaydet & Aktif Et";

        if (isEnglish)
        {
            txtSubtitle.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(88, 166, 255));
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    // Hızlı şablon: Bulut API (OpenAI uyumlu herhangi bir sağlayıcı)
    private void CardCloud_Click(object sender, MouseButtonEventArgs e)
    {
        txtRagBaseUrl.Text    = "https://";
        txtRagModel.Text      = "";
        ollamaHint.Visibility = Visibility.Collapsed;
        pbRagApiKey.Focus();
        txtSubtitle.Text      = Localization.IsEnglish()
            ? "Enter your provider's Base URL, API Key and Model details."
            : "Sağlayıcınızın Base URL, API Key ve Model bilgilerini girin.";
        txtSubtitle.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(88, 166, 255));
    }

    // Hızlı şablon: Yerel Model (localhost sunucusu)
    private void CardLocal_Click(object sender, MouseButtonEventArgs e)
    {
        txtRagBaseUrl.Text    = "http://localhost:11434/v1";
        pbRagApiKey.Password  = "";
        txtRagModel.Text      = "";
        ollamaHint.Visibility = Visibility.Visible;
        txtRagModel.Focus();
        txtSubtitle.Text      = Localization.IsEnglish()
            ? "Enter your local server address and embedding model name."
            : "Yerel sunucunuzun adresini ve embedding model adını girin.";
        txtSubtitle.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(63, 185, 80));
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var baseUrl = txtRagBaseUrl.Text.Trim().TrimEnd('/');
        var apiKey  = pbRagApiKey.Password.Trim();
        var model   = txtRagModel.Text.Trim();

        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(model))
        {
            txtSubtitle.Text = Localization.IsEnglish()
                ? "⚠️ Base URL and Model fields cannot be empty!"
                : "⚠️ Base URL ve Model alanları boş bırakılamaz!";
            txtSubtitle.Foreground = new SolidColorBrush(Colors.OrangeRed);
            return;
        }

        Settings.RagBaseUrl  = baseUrl;
        Settings.RagApiKey   = string.IsNullOrEmpty(apiKey) ? null : apiKey;
        Settings.RagModel    = model;
        Settings.RagEnabled  = true;

        DialogResult = true;
        Close();
    }

    private void BtnDisable_Click(object sender, RoutedEventArgs e)
    {
        Settings.RagEnabled  = false;
        DisabledRequested    = true;
        DialogResult         = true;
        Close();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
