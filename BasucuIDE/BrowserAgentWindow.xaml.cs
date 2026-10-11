using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace mdaiAgent;

/// <summary>
/// Otonom Tarayıcı Ajanı penceresi.
/// BrowserAgentService tarafından kontrol edilir; doğrudan kullanıcı etkileşimi beklenmez.
/// </summary>
public partial class BrowserAgentWindow : Window
{
    public WebView2 WebView => webView;

    private TaskCompletionSource<bool>? _navTcs;

    // Loaded event'i WebView2 init bittiğinde bu tcs'yi çözümler.
    // BrowserAgentService bu tcs'yi await ederek init'in tamamlandığını anlar.
    private readonly TaskCompletionSource<bool> _initTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

#pragma warning disable VSTHRD003 // False positive: RunContinuationsAsynchronously ile oluşturuldu
    public Task WaitForInitAsync() => _initTcs.Task;
#pragma warning restore VSTHRD003

    public BrowserAgentWindow()
    {
        InitializeComponent();
        // WebView2 init'i LOADED eventinde yap — PreviewWindow pattern'ı
        Loaded += BrowserAgentWindow_Loaded;
    }

    // ─────────────────────────────────────────────
    // Init (Loaded event'inde çalışır — pencere Show() edildikten sonra)
    // ─────────────────────────────────────────────

#pragma warning disable VSTHRD100
    private async void BrowserAgentWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Yengi", "BrowserAgentCache");
            Directory.CreateDirectory(userDataFolder);

            var env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder);

            await webView.EnsureCoreWebView2Async(env);

            webView.CoreWebView2.NavigationStarting  += OnNavigationStarting;
            webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            webView.CoreWebView2.NewWindowRequested  += (s, ev) =>
            {
                ev.Handled = true;
                webView.CoreWebView2.Navigate(ev.Uri);
            };

            // Desktop User-Agent — bazı siteler headless tarayıcıları engeller
            webView.CoreWebView2.Settings.UserAgent =
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";
            webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            webView.CoreWebView2.Settings.IsScriptEnabled    = true;

            SetStatus("Hazır ✓", "#4caf50");
            _initTcs.TrySetResult(true);  // Init tamamlandı → service devam edebilir
        }
        catch (Exception ex)
        {
            SetStatus("Init Hatası ✗", "#f44336");
            _initTcs.TrySetException(ex);
        }
    }
#pragma warning restore VSTHRD100

    // ─────────────────────────────────────────────
    // Navigation helpers
    // ─────────────────────────────────────────────

    /// <summary>Belirtilen URL'ye gider; tamamlandığında tcs'yi çözümler.</summary>
    public Task NavigateAsync(string url, TaskCompletionSource<bool> tcs)
    {
        _navTcs = tcs;
        SetStatus("Geziniliyor...", "#ff9800");
        tbCurrentUrl.Text = url;
        webView.CoreWebView2.Navigate(url);
        return Task.CompletedTask;
    }

    // ─────────────────────────────────────────────
    // Events
    // ─────────────────────────────────────────────

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            tbCurrentUrl.Text = e.Uri;
            SetStatus("Yükleniyor...", "#ff9800");
        });
    }

#pragma warning disable VSTHRD100
    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        try
        {
            // Sayfanın tam render edilmesi için kısa bekle
            await Task.Delay(800);

            Dispatcher.Invoke(() =>
            {
                SetStatus(e.IsSuccess ? "Yüklendi ✓" : "Hata ✗", e.IsSuccess ? "#4caf50" : "#f44336");
                tbActionLog.Text = $"Son işlem: {webView.CoreWebView2.DocumentTitle}";
            });

            _navTcs?.TrySetResult(e.IsSuccess);
        }
        catch (Exception ex)
        {
            _navTcs?.TrySetException(ex);
        }
    }
#pragma warning restore VSTHRD100

    private void BrowserAgentWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        // Pencere kapatılmak istenirse gizle, tamamen dispose etme (servis yeniden kullanır)
        e.Cancel = true;
        Hide();
    }

    // ─────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────

    private void SetStatus(string text, string colorHex)
    {
        tbStatus.Text = text;
        ellStatus.Fill = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex));
    }

    public void UpdateActionLog(string message)
    {
        Dispatcher.Invoke(() => tbActionLog.Text = message);
    }
}
