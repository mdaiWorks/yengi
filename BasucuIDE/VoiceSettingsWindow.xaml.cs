using System.Windows;
using System.Windows.Input;

namespace mdaiAgent;

public partial class VoiceSettingsWindow : Window
{
    public AppSettings Settings { get; }

    public VoiceSettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        Settings = settings;
        ApplyLanguage();

        // Kayıtlı değerleri doldur
        txtSttBaseUrl.Text     = settings.SttBaseUrl;
        pbSttApiKey.Password   = settings.SttApiKey ?? "";
        txtSttModel.Text       = settings.SttModel;
    }

    private void ApplyLanguage()
    {
        bool isEnglish = Localization.IsEnglish();

        Title = isEnglish ? LocalizationManager.Instance.GetString("SesliKomutAyarlari") : LocalizationManager.Instance.GetString("SesliKomutAyarlari");
        txtWindowTitle.Text = isEnglish ? LocalizationManager.Instance.GetString("SesliKomutAyarlari") : LocalizationManager.Instance.GetString("SesliKomutAyarlari");
        txtSubtitle.Text = isEnglish
            ? "Enter an API to convert voice recording into text"
            : "Ses kaydını metne çevirmek için bir API girin";

        runHintPrefix.Text = isEnglish ? LocalizationManager.Instance.GetString("Ipucu") : LocalizationManager.Instance.GetString("Ipucu");
        runHintBody.Text = isEnglish ? "You can use any cloud or local service compatible with " : "OpenAI uyumlu, ";
        runHintApi.Text = "Whisper API";
        runHintSuffix.Text = isEnglish
            ? "."
            : " destekleyen herhangi bir bulut veya yerel servis kullanabilirsiniz.";

        txtBaseUrlLabel.Text = "Base URL";
        txtApiKeyLabel.Text = isEnglish ? "API Key" : "API Anahtarı";
        txtModelLabel.Text = isEnglish ? "Model Name" : "Model Adı";
        btnCancel.Content = isEnglish ? "Cancel" : "İptal";
        btnSave.Content = isEnglish ? LocalizationManager.Instance.GetString("Kaydet2") : LocalizationManager.Instance.GetString("Kaydet2");
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var baseUrl = txtSttBaseUrl.Text.Trim().TrimEnd('/');
        var apiKey  = pbSttApiKey.Password.Trim();
        var model   = txtSttModel.Text.Trim();

        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(apiKey))
        {
            txtSubtitle.Text = Localization.IsEnglish()
                ? "⚠️ Base URL and API key cannot be empty!"
                : "⚠️ Base URL ve API Anahtarı boş bırakılamaz!";
            txtSubtitle.Foreground = System.Windows.Media.Brushes.OrangeRed;
            return;
        }

        Settings.SttBaseUrl = baseUrl;
        Settings.SttApiKey  = apiKey;
        Settings.SttModel   = string.IsNullOrEmpty(model) ? "whisper-large-v3" : model;

        DialogResult = true;
        Close();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
