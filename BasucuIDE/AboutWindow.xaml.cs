using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using mdaiAgent.Services;

namespace mdaiAgent;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        ApplyLanguage();

        // Versiyon bilgisini dinamik doldur
        tbVersion.Text = $"v{UpdateService.CurrentVersion}";
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
        btnCheckForUpdates.IsEnabled = false;
        try
        {
            var updateInfo = await UpdateService.Instance.CheckForUpdateAsync();
            if (updateInfo != null)
            {
                var msg = Localization.IsEnglish()
                    ? $"A new version of Yengi is available (v{updateInfo.Version})!\n\nDo you want to download and install the update now?"
                    : $"Yengi'nin yeni bir sürümü mevcut (v{updateInfo.Version})!\n\nŞimdi indirip kurmak ister misiniz?";

                var res = MessageBox.Show(
                    msg,
                    Localization.IsEnglish() ? "New Update Available" : "Yeni Güncelleme Mevcut",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (res == MessageBoxResult.Yes)
                {
                    var mainWindow = Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();
                    Close();
                    if (mainWindow != null)
                    {
                        _ = mainWindow.Dispatcher.InvokeAsync(async () =>
                        {
                            await UpdateService.Instance.DownloadAndInstallUpdateAsync(updateInfo, null, mainWindow);
                        });
                    }
                    else
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = updateInfo.DownloadUrl,
                            UseShellExecute = true
                        });
                    }
                }
            }
            else
            {
                MessageBox.Show(
                    Localization.IsEnglish()
                        ? $"Yengi is up to date (v{UpdateService.CurrentVersion})."
                        : $"Yengi güncel (v{UpdateService.CurrentVersion}).",
                    Localization.IsEnglish() ? "Updates" : "Güncellemeler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
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
