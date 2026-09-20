using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Input;

namespace mdaiAgent;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        ApplyLanguage();

        // Versiyon bilgisini dinamik doldur
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        tbVersion.Text = version != null
            ? $"v{version.Major}.{version.Minor}.{version.Build}"
            : "v1.0.0";
    }

    private void ApplyLanguage()
    {
        bool isEnglish = Localization.IsEnglish();

        Title = LocalizationManager.Instance.GetString("AboutTitle");
        runTagline.Text = LocalizationManager.Instance.GetString("AboutTagline");
        runDeveloperPrefix.Text = LocalizationManager.Instance.GetString("DeveloperPrefix");
        tbFooterText.Text = LocalizationManager.Instance.GetString("AboutFooter");
        btnOk.Content = LocalizationManager.Instance.GetString("AboutOk");
        btnCheckForUpdates.Content = Localization.IsEnglish()
            ? "Check for updates"
            : "Güncellemeleri denetle";
        btnGitHubStar.Content = Localization.IsEnglish()
            ? "Star on GitHub"
            : "GitHub'da Yıldız Ver (Star)";
    }

    // Pencereyi sürükleyerek taşıma
    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BtnEmail_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "mailto:mdaiyazilim@gmail.com",
                UseShellExecute = true
            });
        }
        catch { /* E-posta istemcisi açılamadı */ }
    }

    private void BtnGitHubStar_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/mdaiWorks/yengi",
                UseShellExecute = true
            });
        }
        catch { /* Tarayıcı açılamadı */ }
    }

    private void BtnCheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        _ = CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        const string updateManifestUrl = "https://github.com/mdaiWorks/yengi/releases/latest";

        if (updateManifestUrl.Contains("YOUR_USERNAME", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                Localization.IsEnglish()
                    ? "The update channel has not been configured yet."
                    : "Güncelleme kanalı henüz yapılandırılmadı.",
                Localization.IsEnglish() ? "Updates" : "Güncellemeler",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        btnCheckForUpdates.IsEnabled = false;
        try
        {
            using var client = new System.Net.Http.HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Yengi-IDE/1.0");
            using var response = await client.GetAsync(updateManifestUrl);
            response.EnsureSuccessStatusCode();
            var releaseUrl = response.RequestMessage?.RequestUri?.ToString() ?? updateManifestUrl;

            Process.Start(new ProcessStartInfo
            {
                FileName = releaseUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Localization.IsEnglish()
                    ? $"Update check failed: {ex.Message}"
                    : $"Güncelleme kontrolü başarısız: {ex.Message}",
                Localization.IsEnglish() ? "Updates" : "Güncellemeler",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            btnCheckForUpdates.IsEnabled = true;
        }
    }
}
